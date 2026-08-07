using Blackwall.Infrastructure.Cache.Discord;
using Blackwall.Infrastructure.Persistence;
using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blackwall.Bot.Discord.Services;

public sealed class LockdownService(
    DiscordSocketClient discordClient,
    IServiceScopeFactory scopeFactory,
    ILogger<LockdownService> logger
) {
    private static readonly ulong[] DeniedPermissions =
    [
        (ulong)ChannelPermission.SendMessages,
        (ulong)ChannelPermission.SendMessagesInThreads,
        (ulong)ChannelPermission.CreatePublicThreads
    ];

    /// <summary>
    /// Locks down all text channels and categories in the specified guild by denying
    /// SEND_MESSAGES, SEND_MESSAGES_IN_THREADS, and CREATE_PUBLIC_THREADS for the
    /// @@everyone role via channel-specific permission overwrites.
    /// </summary>
    /// <param name="guildId">The Discord ID of the guild to lock down.</param>
    /// <returns>The number of channels that were successfully locked down.</returns>
    public async Task<int> LockdownAsync(ulong guildId) {
        var guild = discordClient.GetGuild(guildId);
        if (guild is null) {
            logger.LogWarning("Cannot lockdown: guild {GuildId} not found", guildId);
            return 0;
        }

        var everyoneRole = guild.EveryoneRole;
        var channels = GetTargetChannels(guild);
        var count = 0;

        foreach (var channel in channels) {
            try {
                var deny = DeniedPermissions.Aggregate(0UL, (current, perm) => current | perm);
                await channel.AddPermissionOverwriteAsync(everyoneRole,
                    new OverwritePermissions(allowValue: 0, denyValue: deny));
                count++;
            } catch (Exception ex) {
                logger.LogWarning(ex,
                    "Failed to apply lockdown overwrite to channel {ChannelId} in guild {GuildId}",
                    channel.Id, guildId);
            }
        }

        await SetLockdownStateAsync((long)guildId, true);

        logger.LogInformation(
            "Lockdown applied to guild {GuildId}: {Count}/{Total} channels locked",
            guildId, count, channels.Count);

        return count;
    }

    /// <summary>
    /// Removes the lockdown by resetting the @@everyone permission overwrites that were
    /// applied during lockdown, returning those permissions to their inherited state.
    /// </summary>
    /// <param name="guildId">The Discord ID of the guild to unlock.</param>
    /// <returns>The number of channels that were successfully unlocked.</returns>
    public async Task<int> UnlockAsync(ulong guildId) {
        var guild = discordClient.GetGuild(guildId);
        if (guild is null) {
            logger.LogWarning("Cannot unlock: guild {GuildId} not found", guildId);
            return 0;
        }

        var everyoneRole = guild.EveryoneRole;
        var channels = GetTargetChannels(guild);
        var count = 0;

        foreach (var channel in channels) {
            try {
                var existing = channel.GetPermissionOverwrite(everyoneRole);
                if (existing is null)
                    continue;

                var deniedMask = DeniedPermissions.Aggregate(0UL, (current, perm) => current | perm);
                var newDeny = existing.Value.DenyValue & ~deniedMask;

                if (newDeny == 0 && existing.Value.AllowValue == 0) {
                    await channel.RemovePermissionOverwriteAsync(everyoneRole);
                } else {
                    await channel.AddPermissionOverwriteAsync(everyoneRole,
                        new OverwritePermissions(
                            allowValue: existing.Value.AllowValue,
                            denyValue: newDeny
                        ));
                }
                count++;
            } catch (Exception ex) {
                logger.LogWarning(ex,
                    "Failed to remove lockdown overwrite from channel {ChannelId} in guild {GuildId}",
                    channel.Id, guildId);
            }
        }

        await SetLockdownStateAsync((long)guildId, false);

        logger.LogInformation(
            "Lockdown lifted for guild {GuildId}: {Count}/{Total} channels unlocked",
            guildId, count, channels.Count);

        return count;
    }

    /// <summary>
    /// Returns true if the guild is currently marked as locked down in the database.
    /// </summary>
    /// <param name="discordGuildId">The Discord ID of the guild to check.</param>
    /// <returns><see langword="true"/> if the guild is locked down; otherwise <see langword="false"/>.</returns>
    public async Task<bool> IsLockedDownAsync(long discordGuildId) {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BlackwallDbContext>();
        var config = await dbContext.GuildInstances
            .Where(x => x.DiscordGuildId == discordGuildId)
            .Select(x => x.SpamConfiguration.IsLockedDown)
            .FirstOrDefaultAsync();
        return config;
    }

    /// <summary>
    /// Fetches all text-based channels and categories from the guild.
    /// </summary>
    /// <param name="guild">The guild whose channels should be retrieved.</param>
    /// <returns>A list of <see cref="SocketGuildChannel"/> instances targeted for lockdown operations.</returns>
    private static List<SocketGuildChannel> GetTargetChannels(SocketGuild guild) {
        return guild.Channels
            .Where(c => c is SocketTextChannel or SocketCategoryChannel or SocketForumChannel)
            .ToList();
    }

    /// <summary>
    /// Persists the lockdown state to the database and invalidates the cache.
    /// </summary>
    /// <param name="discordGuildId">The Discord ID of the guild whose lockdown state is being updated.</param>
    /// <param name="lockedDown">The lockdown state to persist — <see langword="true"/> for locked, <see langword="false"/> for unlocked.</param>
    /// <exception cref="DbUpdateException">Thrown when the database update fails during <see>
    ///         <cref>DbContext.SaveChangesAsync</cref>
    ///     </see>
    ///</exception>
    private async Task SetLockdownStateAsync(long discordGuildId, bool lockedDown) {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BlackwallDbContext>();
        var cache = scope.ServiceProvider.GetRequiredService<SpamConfigurationCache>();

        var config = await dbContext.GuildInstances
            .Include(x => x.SpamConfiguration)
            .FirstOrDefaultAsync(x => x.DiscordGuildId == discordGuildId);

        if (config is null)
            return;

        config.SpamConfiguration.IsLockedDown = lockedDown;
        config.SpamConfiguration.UpdatedAtUtc = DateTime.UtcNow;
        config.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();
        await cache.InvalidateAsync(discordGuildId);
    }
}
