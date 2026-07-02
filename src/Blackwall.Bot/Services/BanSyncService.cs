using Blackwall.Core.Entities;
using Blackwall.Infrastructure.Persistence;
using Discord;
using Discord.Rest;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blackwall.Bot.Services;

public sealed class BanSyncService(
    DiscordSocketClient discordClient,
    IServiceScopeFactory scopeFactory,
    ILogger<BanSyncService> logger
) {
    /// <summary>
    /// Synchronizes the ban list for the specified guild from Discord into the database.
    /// Removes bans that no longer exist in Discord and adds/updates bans that do.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID whose bans should be synced.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The number of bans synced.</returns>
    public async Task<int> SyncBansAsync(long discordGuildId, CancellationToken cancellationToken = default) {
        var guild = discordClient.GetGuild((ulong)discordGuildId);
        if (guild is null) {
            logger.LogWarning("Cannot sync bans: guild {GuildId} not found in bot cache", discordGuildId);
            return 0;
        }

        var discordBans = new List<RestBan>();
        try {
            await foreach (var batch in guild.GetBansAsync(limit: 1000, options: new RequestOptions { CancelToken = cancellationToken }).WithCancellation(cancellationToken))
                discordBans.AddRange(batch);
        } catch (Exception ex) {
            logger.LogError(ex, "Failed to fetch bans from Discord for guild {GuildId}", discordGuildId);
            throw;
        }

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BlackwallDbContext>();

        var instance = await dbContext.GuildInstances
            .Include(x => x.Bans)
            .FirstOrDefaultAsync(x => x.DiscordGuildId == discordGuildId, cancellationToken);

        if (instance is null) {
            logger.LogWarning("Cannot sync bans: guild instance {GuildId} not found in database", discordGuildId);
            return 0;
        }

        var existingBans = instance.Bans.ToDictionary(b => b.DiscordUserId);
        var discordBanUserIds = discordBans.Select(b => (long)b.User.Id).ToHashSet();

        foreach (var existing in existingBans.Values.Where(existing => !discordBanUserIds.Contains(existing.DiscordUserId))) {
            dbContext.GuildBans.Remove(existing);
        }

        foreach (var ban in discordBans) {
            cancellationToken.ThrowIfCancellationRequested();

            if (existingBans.TryGetValue((long)ban.User.Id, out var existing)) {
                existing.Username = ban.User.Username;
                existing.Reason = ban.Reason;
            } else {
                dbContext.GuildBans.Add(new GuildBan {
                    GuildInstanceId = instance.Id,
                    DiscordUserId = (long)ban.User.Id,
                    Username = ban.User.Username,
                    Reason = ban.Reason,
                    BannedAtUtc = DateTime.UtcNow
                });
            }
        }

        instance.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Synced {Count} bans for guild {GuildId}", discordBans.Count, discordGuildId);
        return discordBans.Count;
    }

    /// <summary>
    /// Synchronizes bans for all active guilds from Discord into the database.
    /// Errors for individual guilds are logged and swallowed so one failure doesn't stop the rest.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task SyncAllBansAsync(CancellationToken cancellationToken = default) {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BlackwallDbContext>();

        var guildIds = await dbContext.GuildInstances
            .Where(x => x.IsActive)
            .Select(x => x.DiscordGuildId)
            .ToListAsync(cancellationToken);

        logger.LogInformation("Syncing bans for {Count} active guild(s)", guildIds.Count);

        foreach (var guildId in guildIds) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                await SyncBansAsync(guildId, cancellationToken);
            } catch (OperationCanceledException) when (stoppingTokenIsCancellation(cancellationToken)) {
                throw;
            } catch (Exception ex) {
                logger.LogWarning(ex, "Failed to sync bans for guild {GuildId}", guildId);
            }
        }
    }

    private static bool stoppingTokenIsCancellation(CancellationToken ct) => ct.IsCancellationRequested;

    /// <summary>
    /// Imports bans from a source guild into the target guild. Only imports bans from guilds
    /// that have sharing enabled. Skips users who are already banned in the target guild.
    /// </summary>
    /// <param name="targetDiscordGuildId">The Discord guild ID to import bans into.</param>
    /// <param name="sourceDiscordGuildId">The Discord guild ID to import bans from.</param>
    /// <param name="discordUserIds">Optional list of specific user IDs to import. If null, imports all.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A tuple of (imported, skipped, failed, errors).</returns>
    public async Task<(int Imported, int Skipped, int Failed, List<string> Errors)> ImportBansAsync(
        long targetDiscordGuildId,
        long sourceDiscordGuildId,
        IReadOnlyList<long>? discordUserIds,
        CancellationToken cancellationToken = default
    ) {
        var errors = new List<string>();

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BlackwallDbContext>();

        var sourceInstance = await dbContext.GuildInstances
            .Include(x => x.Bans)
            .FirstOrDefaultAsync(x => x.DiscordGuildId == sourceDiscordGuildId && x.IsActive, cancellationToken);

        if (sourceInstance is null) {
            return (0, 0, 0, [new($"Source guild {sourceDiscordGuildId} not found.")]);
        }

        if (!sourceInstance.ShareBanList) {
            return (0, 0, 0, [new($"Source guild {sourceDiscordGuildId} does not have ban list sharing enabled.")]);
        }

        var targetGuild = discordClient.GetGuild((ulong)targetDiscordGuildId);
        if (targetGuild is null) {
            return (0, 0, 0, [new($"Target guild {targetDiscordGuildId} not found in bot cache.")]);
        }

        var targetInstance = await dbContext.GuildInstances
            .Include(x => x.Bans)
            .FirstOrDefaultAsync(x => x.DiscordGuildId == targetDiscordGuildId, cancellationToken);

        if (targetInstance is null) {
            return (0, 0, 0, [new($"Target guild {targetDiscordGuildId} not found in database.")]);
        }

        var sourceBans = sourceInstance.Bans.AsQueryable();
        if (discordUserIds is not null && discordUserIds.Count > 0) {
            var idSet = discordUserIds.ToHashSet();
            sourceBans = sourceBans.Where(b => idSet.Contains(b.DiscordUserId)).AsQueryable();
        }

        var bansToImport = sourceBans.ToList();
        var imported = 0;
        var skipped = 0;
        var failed = 0;

        var targetExistingUserIds = targetInstance.Bans.Select(b => b.DiscordUserId).ToHashSet();

        foreach (var sourceBan in bansToImport) {
            cancellationToken.ThrowIfCancellationRequested();

            if (targetExistingUserIds.Contains(sourceBan.DiscordUserId)) {
                skipped++;
                continue;
            }

            try {
                var existingBan = await targetGuild.GetBanAsync((ulong)sourceBan.DiscordUserId, options: new RequestOptions { CancelToken = cancellationToken });
                if (existingBan is not null) {
                    dbContext.GuildBans.Add(new GuildBan {
                        GuildInstanceId = targetInstance.Id,
                        DiscordUserId = sourceBan.DiscordUserId,
                        Username = sourceBan.Username,
                        Reason = sourceBan.Reason,
                        BannedAtUtc = sourceBan.BannedAtUtc
                    });
                    targetExistingUserIds.Add(sourceBan.DiscordUserId);
                    skipped++;
                    continue;
                }

                var reason = string.IsNullOrWhiteSpace(sourceBan.Reason)
                    ? $"Imported from shared ban list (guild {sourceDiscordGuildId})"
                    : sourceBan.Reason;

                await targetGuild.AddBanAsync((ulong)sourceBan.DiscordUserId, pruneDays: 0, reason: reason, options: new RequestOptions { CancelToken = cancellationToken });

                dbContext.GuildBans.Add(new GuildBan {
                    GuildInstanceId = targetInstance.Id,
                    DiscordUserId = sourceBan.DiscordUserId,
                    Username = sourceBan.Username,
                    Reason = reason,
                    BannedAtUtc = DateTime.UtcNow
                });
                targetExistingUserIds.Add(sourceBan.DiscordUserId);
                imported++;
            } catch (Exception ex) {
                failed++;
                errors.Add($"Failed to ban user {sourceBan.DiscordUserId}: {ex.Message}");
                logger.LogWarning(ex, "Failed to import ban for user {UserId} into guild {GuildId}", sourceBan.DiscordUserId, targetDiscordGuildId);
            }
        }

        targetInstance.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Ban import to guild {TargetGuildId} from guild {SourceGuildId}: {Imported} imported, {Skipped} skipped, {Failed} failed",
            targetDiscordGuildId, sourceDiscordGuildId, imported, skipped, failed);

        return (imported, skipped, failed, errors);
    }
}
