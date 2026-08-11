using Blackwall.Core.Entities;
using Blackwall.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blackwall.Bot.Twitch;

public sealed class TwitchBanSyncService(
    TwitchBotService twitchBotService,
    IServiceScopeFactory scopeFactory,
    ILogger<TwitchBanSyncService> logger
) {
    public async Task<int> SyncBansAsync(long twitchUserId, CancellationToken cancellationToken = default) {
        var helixBans = await twitchBotService.GetChannelBansAsync(twitchUserId, cancellationToken);

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BlackwallDbContext>();

        var instance = await dbContext.TwitchChannelInstances
            .Include(x => x.Bans)
            .FirstOrDefaultAsync(x => x.TwitchUserId == twitchUserId, cancellationToken);

        if (instance is null) {
            logger.LogWarning("Cannot sync bans: channel instance {TwitchUserId} not found in database", twitchUserId);
            return 0;
        }

        var existingBans = instance.Bans.ToDictionary(b => b.TwitchUserId);
        var helixBanUserIds = helixBans.Select(b => b.UserId).ToHashSet();

        foreach (var existing in existingBans.Values.Where(existing => !helixBanUserIds.Contains(existing.TwitchUserId))) {
            dbContext.TwitchChannelBans.Remove(existing);
        }

        foreach (var ban in helixBans) {
            cancellationToken.ThrowIfCancellationRequested();

            if (existingBans.TryGetValue(ban.UserId, out var existing)) {
                existing.Username = ban.Username;
                existing.Reason = ban.Reason;
                if (ban.BannedAtUtc.HasValue)
                    existing.BannedAtUtc = ban.BannedAtUtc;
            } else {
                dbContext.TwitchChannelBans.Add(new TwitchChannelBan {
                    TwitchChannelInstanceId = instance.Id,
                    TwitchUserId = ban.UserId,
                    Username = ban.Username,
                    Reason = ban.Reason,
                    BannedAtUtc = ban.BannedAtUtc ?? DateTime.UtcNow
                });
            }
        }

        instance.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Synced {Count} bans for Twitch channel {TwitchUserId}", helixBans.Count, twitchUserId);
        return helixBans.Count;
    }

    public async Task SyncAllBansAsync(CancellationToken cancellationToken = default) {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BlackwallDbContext>();

        var channelIds = await dbContext.TwitchChannelInstances
            .Where(x => x.IsActive)
            .Select(x => x.TwitchUserId)
            .ToListAsync(cancellationToken);

        logger.LogInformation("Syncing bans for {Count} active Twitch channel(s)", channelIds.Count);

        foreach (var channelId in channelIds) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                await SyncBansAsync(channelId, cancellationToken);
            } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
            } catch (Exception ex) {
                logger.LogWarning(ex, "Failed to sync bans for Twitch channel {TwitchUserId}", channelId);
            }
        }
    }

    public async Task ProcessAutoSyncRulesAsync(CancellationToken cancellationToken = default) {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BlackwallDbContext>();

        var rules = await dbContext.TwitchChannelBanSyncRules
            .Where(r => r.IsEnabled)
            .ToListAsync(cancellationToken);

        if (rules.Count == 0)
            return;

        logger.LogInformation("Processing {Count} Twitch ban auto-sync rule(s)", rules.Count);

        foreach (var rule in rules) {
            cancellationToken.ThrowIfCancellationRequested();

            var targetInstance = await dbContext.TwitchChannelInstances
                .FirstOrDefaultAsync(x => x.Id == rule.TargetTwitchChannelInstanceId && x.IsActive, cancellationToken);

            if (targetInstance is null) {
                logger.LogWarning("Auto-sync rule {RuleId}: target channel not found or inactive", rule.Id);
                continue;
            }

            var sourceInstance = await dbContext.TwitchChannelInstances
                .FirstOrDefaultAsync(x => x.TwitchUserId == rule.SourceTwitchUserId && x.IsActive && x.ShareBanList, cancellationToken);

            if (sourceInstance is null) {
                logger.LogWarning("Auto-sync rule {RuleId}: source channel {SourceTwitchUserId} not found, inactive, or not sharing", rule.Id, rule.SourceTwitchUserId);
                continue;
            }

            try {
                var (imported, skipped, failed, _) = await ImportBansAsync(
                    targetInstance.TwitchUserId, rule.SourceTwitchUserId, null, cancellationToken);

                rule.LastSyncedAtUtc = DateTime.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);

                logger.LogInformation("Auto-sync rule {RuleId}: {Imported} imported, {Skipped} skipped, {Failed} failed",
                    rule.Id, imported, skipped, failed);
            } catch (Exception ex) {
                logger.LogWarning(ex, "Auto-sync rule {RuleId} failed for target channel {TargetTwitchUserId} from source channel {SourceTwitchUserId}",
                    rule.Id, targetInstance.TwitchUserId, rule.SourceTwitchUserId);
            }
        }
    }

    public async Task<(int Imported, int Skipped, int Failed, List<string> Errors)> ImportBansAsync(
        long targetTwitchUserId,
        long sourceTwitchUserId,
        IReadOnlyList<long>? twitchUserIds,
        CancellationToken cancellationToken = default
    ) {
        var errors = new List<string>();

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BlackwallDbContext>();

        var sourceInstance = await dbContext.TwitchChannelInstances
            .Include(x => x.Bans)
            .FirstOrDefaultAsync(x => x.TwitchUserId == sourceTwitchUserId && x.IsActive, cancellationToken);

        if (sourceInstance is null) {
            return (0, 0, 0, [new($"Source channel {sourceTwitchUserId} not found.")]);
        }

        if (!sourceInstance.ShareBanList) {
            return (0, 0, 0, [new($"Source channel {sourceTwitchUserId} does not have ban list sharing enabled.")]);
        }

        var targetInstance = await dbContext.TwitchChannelInstances
            .Include(x => x.Bans)
            .FirstOrDefaultAsync(x => x.TwitchUserId == targetTwitchUserId, cancellationToken);

        if (targetInstance is null) {
            return (0, 0, 0, [new($"Target channel {targetTwitchUserId} not found in database.")]);
        }

        var sourceBans = sourceInstance.Bans.AsQueryable();
        if (twitchUserIds is not null && twitchUserIds.Count > 0) {
            var idSet = twitchUserIds.ToHashSet();
            sourceBans = sourceBans.Where(b => idSet.Contains(b.TwitchUserId)).AsQueryable();
        }

        var bansToImport = sourceBans.ToList();
        var imported = 0;
        var skipped = 0;
        var failed = 0;

        var targetExistingUserIds = targetInstance.Bans.Select(b => b.TwitchUserId).ToHashSet();

        foreach (var sourceBan in bansToImport) {
            cancellationToken.ThrowIfCancellationRequested();

            if (targetExistingUserIds.Contains(sourceBan.TwitchUserId)) {
                skipped++;
                continue;
            }

            try {
                var reason = string.IsNullOrWhiteSpace(sourceBan.Reason)
                    ? $"Imported from shared ban list (channel {sourceTwitchUserId})"
                    : sourceBan.Reason;

                await twitchBotService.BanUserAsync(targetTwitchUserId, sourceBan.TwitchUserId, reason);

                dbContext.TwitchChannelBans.Add(new TwitchChannelBan {
                    TwitchChannelInstanceId = targetInstance.Id,
                    TwitchUserId = sourceBan.TwitchUserId,
                    Username = sourceBan.Username,
                    Reason = reason,
                    BannedAtUtc = DateTime.UtcNow
                });
                targetExistingUserIds.Add(sourceBan.TwitchUserId);
                imported++;
            } catch (Exception ex) {
                failed++;
                errors.Add($"Failed to ban user {sourceBan.TwitchUserId}: {ex.Message}");
                logger.LogWarning(ex, "Failed to import ban for user {UserId} into Twitch channel {TwitchUserId}", sourceBan.TwitchUserId, targetTwitchUserId);
            }
        }

        targetInstance.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Ban import to Twitch channel {TargetTwitchUserId} from channel {SourceTwitchUserId}: {Imported} imported, {Skipped} skipped, {Failed} failed",
            targetTwitchUserId, sourceTwitchUserId, imported, skipped, failed);

        return (imported, skipped, failed, errors);
    }
}
