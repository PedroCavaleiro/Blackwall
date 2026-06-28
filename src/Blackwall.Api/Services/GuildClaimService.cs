using Blackwall.Core.DTOs;
using Blackwall.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Blackwall.Api.Services;

public sealed class GuildClaimService(BlackwallDbContext dbContext)
{
    private const ulong Administrator = 8;
    private const ulong ManageGuild = 32;

    /// <summary>
    /// Syncs manageable Discord guilds against the local guild instances, updating metadata
    /// and assigning the authenticated user as owner for any guild they own that has no owner set.
    /// </summary>
    public async Task ClaimOwnershipAsync(
        long appUserId,
        IReadOnlyList<DiscordGuildDto> guilds,
        CancellationToken cancellationToken = default
    ) {
        var manageableGuilds = guilds.Where(CanManage).ToList();
        var guildIds = guilds.Select(x => x.Id).Select(long.Parse).AsEnumerable();

        var existingGuilds = await dbContext.GuildInstances
            .Where(x => guildIds.Contains(x.DiscordGuildId))
            .ToListAsync(cancellationToken);

        foreach (var guild in manageableGuilds) {
            var existing = existingGuilds.FirstOrDefault(x => x.DiscordGuildId == long.Parse(guild.Id));

            if (existing is null)
                continue;

            existing.Name = guild.Name;
            existing.IconHash = guild.Icon;
            existing.IsActive = true;
            existing.UpdatedAtUtc = DateTime.UtcNow;

            if (existing.OwnerUserId is null && guild.Owner)
                existing.OwnerUserId = appUserId;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Returns a list of Discord guilds the user can manage, enriched with local guild instance data.
    /// Each entry includes whether the bot is installed, whether ownership has been claimed,
    /// and whether the user can open the guild dashboard. Results are ordered by ownership then name.
    /// </summary>
    /// <param name="appUserId">The internal ID of the authenticated user.</param>
    /// <param name="guilds">The list of Discord guilds returned from the Discord API.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A read-only list of <see cref="ManageableGuildResponse"/> for all guilds the user can manage.</returns>
    public async Task<IReadOnlyList<ManageableGuildResponse>> GetManageableGuildsAsync(
    long appUserId,
    IReadOnlyList<DiscordGuildDto> guilds,
    CancellationToken cancellationToken = default)
{
    var manageableGuilds = guilds
        .Where(CanManage)
        .ToList();

    var guildIds = guilds.Select(x => x.Id).Select(long.Parse).AsEnumerable();

    var existingGuilds = await dbContext.GuildInstances
        .Where(x => guildIds.Contains(x.DiscordGuildId))
        .Select(x => new {
            x.Id,
            x.DiscordGuildId,
            x.IsActive,
            x.OwnerUserId
        })
        .ToListAsync(cancellationToken);

    var guildMap = existingGuilds.ToDictionary(x => x.DiscordGuildId);

    var guildInstanceIds = existingGuilds
        .Select(x => x.Id)
        .AsEnumerable();

    var managerGuildIds = await dbContext.GuildManagers
        .Where(x => guildInstanceIds.Contains(x.GuildInstanceId) && x.UserId == appUserId)
        .Select(x => x.GuildInstanceId)
        .ToListAsync(cancellationToken);

    var managerSet = managerGuildIds.ToHashSet();

    return manageableGuilds
        .Select(guild => {
            guildMap.TryGetValue(long.Parse(guild.Id), out var existing);

            var botInstalled = existing is not null && existing.IsActive;
            var claimed = existing?.OwnerUserId is not null;
            var isManager = existing is not null && managerSet.Contains(existing.Id);

            var canOpen = existing is not null
                          && existing.IsActive
                          && (existing.OwnerUserId == appUserId || guild.Owner || isManager);

            return new ManageableGuildResponse(
                DiscordGuildId: long.Parse(guild.Id),
                Name: guild.Name,
                Icon: guild.Icon,
                Owner: guild.Owner,
                CanManage: true,
                BotInstalled: botInstalled,
                Claimed: claimed,
                CanOpen: canOpen
            );
        })
        .OrderByDescending(x => x.Owner)
        .ThenBy(x => x.Name)
        .ToList();
}

    /// <summary>
    /// Determines whether the specified user has permission to open the dashboard for a guild.
    /// Returns true if the user is the owner or an explicit manager of the active guild instance.
    /// </summary>
    public async Task<bool> CanOpenGuildAsync(
        long appUserId,
        long discordGuildId,
        CancellationToken cancellationToken = default)
    {
        var instance = await dbContext.GuildInstances
            .Where(x => x.DiscordGuildId == discordGuildId && x.IsActive)
            .Select(x => new { x.Id, x.OwnerUserId })
            .FirstOrDefaultAsync(cancellationToken);

        if (instance is null)
            return false;

        if (instance.OwnerUserId == appUserId)
            return true;

        return await dbContext.GuildManagers
            .AnyAsync(x => x.GuildInstanceId == instance.Id && x.UserId == appUserId, cancellationToken);
    }

    /// <summary>
    /// Determines whether a user has sufficient permissions to manage a guild.
    /// </summary>
    private static bool CanManage(DiscordGuildDto guild) {
        return guild.Owner
               || (guild.Permissions & Administrator) == Administrator
               || (guild.Permissions & ManageGuild) == ManageGuild;
    }
}