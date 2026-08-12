using Blackwall.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blackwall.Modules.Banlist;

public sealed class BanSyncService(
    IBanPlatformProvider platformProvider,
    IBanSyncDataAccess dataAccess,
    IServiceScopeFactory scopeFactory,
    ILogger<BanSyncService> logger
) {
    public async Task<int> SyncBansAsync(long platformId, CancellationToken cancellationToken = default) {
        var platformBans = await platformProvider.FetchBansAsync(platformId, cancellationToken);

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BlackwallDbContext>();

        var instanceData = await dataAccess.GetInstanceWithBansAsync(dbContext, platformId, cancellationToken);
        if (instanceData is null) {
            logger.LogWarning("Cannot sync bans: {Platform} instance {PlatformId} not found in database", platformProvider.PlatformName, platformId);
            return 0;
        }

        var (instanceId, existingBans) = instanceData.Value;
        var existingDict = existingBans.ToDictionary(b => b.UserId);
        var platformBanUserIds = platformBans.Select(b => b.UserId).ToHashSet();

        foreach (var existing in existingDict.Values.Where(e => !platformBanUserIds.Contains(e.UserId))) {
            dataAccess.RemoveBan(dbContext, existing);
        }

        foreach (var ban in platformBans) {
            cancellationToken.ThrowIfCancellationRequested();

            if (existingDict.TryGetValue(ban.UserId, out var existing)) {
                dataAccess.UpdateBan(dbContext, existing, ban);
            } else {
                dataAccess.AddBan(dbContext, instanceId, new BanEntry(
                    0, ban.UserId, ban.Username, ban.Reason, ban.BannedAtUtc ?? DateTime.UtcNow
                ));
            }
        }

        await dataAccess.UpdateInstanceTimestampAsync(dbContext, instanceId, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Synced {Count} bans for {Platform} {PlatformId}", platformBans.Count, platformProvider.PlatformName, platformId);
        return platformBans.Count;
    }

    public async Task SyncAllBansAsync(CancellationToken cancellationToken = default) {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BlackwallDbContext>();

        var platformIds = await dataAccess.GetActivePlatformIdsAsync(dbContext, cancellationToken);

        logger.LogInformation("Syncing bans for {Count} active {Platform} instance(s)", platformIds.Count, platformProvider.PlatformName);

        foreach (var platformId in platformIds) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                await SyncBansAsync(platformId, cancellationToken);
            } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
            } catch (Exception ex) {
                logger.LogWarning(ex, "Failed to sync bans for {Platform} {PlatformId}", platformProvider.PlatformName, platformId);
            }
        }
    }

    public async Task ProcessAutoSyncRulesAsync(CancellationToken cancellationToken = default) {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BlackwallDbContext>();

        var rules = await dataAccess.GetEnabledSyncRulesAsync(dbContext, cancellationToken);
        if (rules.Count == 0)
            return;

        logger.LogInformation("Processing {Count} {Platform} ban auto-sync rule(s)", rules.Count, platformProvider.PlatformName);

        foreach (var rule in rules) {
            cancellationToken.ThrowIfCancellationRequested();

            var targetInfo = await dataAccess.GetSyncRuleTargetAsync(dbContext, rule, cancellationToken);
            if (targetInfo is null || !targetInfo.Value.IsActive) {
                logger.LogWarning("Auto-sync rule {RuleId}: target {Platform} not found or inactive", rule.Id, platformProvider.PlatformName);
                continue;
            }

            var sourceInfo = await dataAccess.GetSyncRuleSourceAsync(dbContext, rule, cancellationToken);
            if (sourceInfo is null || !sourceInfo.Value.IsActive || !sourceInfo.Value.ShareBanList) {
                logger.LogWarning("Auto-sync rule {RuleId}: source {Platform} {SourceId} not found, inactive, or not sharing",
                    rule.Id, platformProvider.PlatformName, rule.SourcePlatformId);
                continue;
            }

            try {
                var result = await ImportBansAsync(targetInfo.Value.TargetPlatformId, sourceInfo.Value.SourcePlatformId, null, cancellationToken);

                await dataAccess.UpdateSyncRuleTimestampAsync(dbContext, rule, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);

                logger.LogInformation("Auto-sync rule {RuleId}: {Imported} imported, {Skipped} skipped, {Failed} failed",
                    rule.Id, result.Imported, result.Skipped, result.Failed);
            } catch (Exception ex) {
                logger.LogWarning(ex, "Auto-sync rule {RuleId} failed for target {Platform} {TargetId} from source {SourceId}",
                    rule.Id, platformProvider.PlatformName, targetInfo.Value.TargetPlatformId, rule.SourcePlatformId);
            }
        }
    }

    public async Task<BanImportResult> ImportBansAsync(
        long targetPlatformId,
        long sourcePlatformId,
        IReadOnlyList<long>? userIds,
        CancellationToken cancellationToken = default
    ) {
        var errors = new List<string>();

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BlackwallDbContext>();

        var sourceData = await dataAccess.GetSourceInstanceWithBansAsync(dbContext, sourcePlatformId, cancellationToken);
        if (sourceData is null) {
            return new BanImportResult(0, 0, 0, [$"Source {platformProvider.PlatformName} {sourcePlatformId} not found."]);
        }

        var (sourceInstanceId, shareBanList, sourceBans) = sourceData.Value;
        if (!shareBanList) {
            return new BanImportResult(0, 0, 0, [$"Source {platformProvider.PlatformName} {sourcePlatformId} does not have ban list sharing enabled."]);
        }

        var targetData = await dataAccess.GetTargetInstanceWithBansAsync(dbContext, targetPlatformId, cancellationToken);
        if (targetData is null) {
            return new BanImportResult(0, 0, 0, [$"Target {platformProvider.PlatformName} {targetPlatformId} not found in database."]);
        }

        var (targetInstanceId, targetBans) = targetData.Value;

        var filteredSourceBans = sourceBans.AsQueryable();
        if (userIds is not null && userIds.Count > 0) {
            var idSet = userIds.ToHashSet();
            filteredSourceBans = filteredSourceBans.Where(b => idSet.Contains(b.UserId)).AsQueryable();
        }

        var bansToImport = filteredSourceBans.ToList();
        var imported = 0;
        var skipped = 0;
        var failed = 0;

        var targetExistingUserIds = targetBans.Select(b => b.UserId).ToHashSet();

        foreach (var sourceBan in bansToImport) {
            cancellationToken.ThrowIfCancellationRequested();

            if (targetExistingUserIds.Contains(sourceBan.UserId)) {
                skipped++;
                continue;
            }

            try {
                var reason = string.IsNullOrWhiteSpace(sourceBan.Reason)
                    ? $"Imported from shared ban list ({platformProvider.PlatformName} {sourcePlatformId})"
                    : sourceBan.Reason;

                await platformProvider.BanUserAsync(targetPlatformId, sourceBan.UserId, reason, cancellationToken);

                dataAccess.AddBan(dbContext, targetInstanceId, new BanEntry(
                    0, sourceBan.UserId, sourceBan.Username, reason, DateTime.UtcNow
                ));
                targetExistingUserIds.Add(sourceBan.UserId);
                imported++;
            } catch (Exception ex) {
                failed++;
                errors.Add($"Failed to ban user {sourceBan.UserId}: {ex.Message}");
                logger.LogWarning(ex, "Failed to import ban for user {UserId} into {Platform} {PlatformId}",
                    sourceBan.UserId, platformProvider.PlatformName, targetPlatformId);
            }
        }

        await dataAccess.UpdateInstanceTimestampAsync(dbContext, targetInstanceId, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Ban import to {Platform} {TargetId} from {SourceId}: {Imported} imported, {Skipped} skipped, {Failed} failed",
            platformProvider.PlatformName, targetPlatformId, sourcePlatformId, imported, skipped, failed);

        return new BanImportResult(imported, skipped, failed, errors);
    }
}
