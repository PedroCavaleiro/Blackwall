using Blackwall.Infrastructure.Persistence;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Blackwall.Bot.Discord.Services;

public sealed class GuildPermissionSyncService(
    BlackwallDbContext dbContext,
    DiscordSocketClient discordClient,
    ILogger<GuildPermissionSyncService> logger
) {
    /// <summary>
    /// Synchronizes all active guild instances with the bot's cached guild data,
    /// updating name, icon, and owner information. Guilds no longer present in
    /// the bot cache are marked as inactive.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task SyncAsync(CancellationToken cancellationToken = default) {
        var activeGuilds = await dbContext.GuildInstances
            .Include(x => x.Managers)
            .Where(x => x.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var guildInstance in activeGuilds) {
            cancellationToken.ThrowIfCancellationRequested();

            var socketGuild = discordClient.GetGuild((ulong)guildInstance.DiscordGuildId);

            if (socketGuild is null) {
                logger.LogWarning(
                    "Guild {GuildId} is marked active but is not present in the bot cache",
                    guildInstance.DiscordGuildId
                );

                guildInstance.IsActive = false;
                guildInstance.UpdatedAtUtc = DateTime.UtcNow;
                continue;
            }

            guildInstance.Name = socketGuild.Name;
            guildInstance.IconHash = socketGuild.IconId;
            guildInstance.UpdatedAtUtc = DateTime.UtcNow;

            var ownerUser = await dbContext.AppUsers
                .FirstOrDefaultAsync(
                    x => x.DiscordUserId == (long)socketGuild.OwnerId,
                    cancellationToken
                );

            if (ownerUser is not null)
                guildInstance.OwnerUserId = ownerUser.Id;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}