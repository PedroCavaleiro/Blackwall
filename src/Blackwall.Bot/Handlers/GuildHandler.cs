using Blackwall.Core.Entities;
using Blackwall.Infrastructure.Persistence;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blackwall.Bot.Handlers;

public sealed class GuildHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<GuildHandler> logger
) {
    /// <summary>
    /// Called when the bot joins a guild. Creates a new guild instance if one does not exist,
    /// or reactivates and updates an existing one. Resolves the guild owner to a local user if possible.
    /// </summary>
    /// <param name="guild">The Discord guild that was joined.</param>
    public async Task OnJoinedGuildAsync(SocketGuild guild) {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BlackwallDbContext>();

        var existing = await dbContext.GuildInstances
            .Include(x => x.SpamConfiguration)
            .FirstOrDefaultAsync(x => x.DiscordGuildId == (long)guild.Id);

        if (existing is not null) {
            existing.Name = guild.Name;
            existing.IconHash = guild.IconId;
            existing.IsActive = true;
            existing.UpdatedAtUtc = DateTime.UtcNow;

            if (existing.OwnerUserId is null) {
                var ownerUser = await dbContext.AppUsers
                    .FirstOrDefaultAsync(x => x.DiscordUserId == (long)guild.OwnerId);

                if (ownerUser is not null)
                    existing.OwnerUserId = ownerUser.Id;
            }

            await dbContext.SaveChangesAsync();
            return;
        }

        var owner = await dbContext.AppUsers
            .FirstOrDefaultAsync(x => x.DiscordUserId == (long)guild.OwnerId);

        var guildInstance = new GuildInstance {
            DiscordGuildId = (long)guild.Id,
            Name = guild.Name,
            IconHash = guild.IconId,
            IsActive = true,
            OwnerUserId = owner?.Id,
            UpdatedAtUtc = DateTime.UtcNow,
            SpamConfiguration = new SpamConfiguration {
                MaxMessagesPerWindow = 5,
                RateLimitWindowSeconds = 8,
                DuplicateMessageThreshold = 3,
                DuplicateWindowSeconds = 5,
                DuplicateCrossChannelEnabled = true,
                MentionLimit = 5,
                BlockInviteLinks = true,
                BlockSuspiciousLinks = false,
                IsEnabled = true,
                IsDryRun = false,
                Action = InfractionAction.DeleteOnly,
                LogChannelId = null,
                MessageDeleteDays = 0,
                IsAntiRaidEnabled = false,
                AntiRaidJoinThreshold = 10,
                AntiRaidWindowSeconds = 30,
                AntiRaidCooldownMinutes = 30
            }
        };

        dbContext.GuildInstances.Add(guildInstance);
        await dbContext.SaveChangesAsync();

        logger.LogInformation(
            "Guild instance created for Discord guild {GuildId} with owner user {OwnerUserId}",
            guild.Id,
            guildInstance.OwnerUserId);
    }

    /// <summary>
    /// Called when the bot leaves or is removed from a guild. Marks the guild instance as inactive.
    /// </summary>
    /// <param name="guild">The Discord guild that was left.</param>
    public async Task OnLeftGuildAsync(SocketGuild guild) {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BlackwallDbContext>();

        var existing = await dbContext.GuildInstances
            .FirstOrDefaultAsync(x => x.DiscordGuildId == (long)guild.Id);

        if (existing is null)
            return;

        existing.IsActive = false;
        existing.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();
    }
}