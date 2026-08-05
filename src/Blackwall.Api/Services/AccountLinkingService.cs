using Blackwall.Core.DTOs;
using Blackwall.Core.Entities;
using Blackwall.Infrastructure.Persistence;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;

namespace Blackwall.Api.Services;

public sealed class AccountLinkingService(
    BlackwallDbContext dbContext,
    DiscordSocketClient discordClient
) {

    public async Task<LinkedAccountsResponse> GetLinkedAccountsAsync(long appUserId, CancellationToken cancellationToken = default) {
        var user = await dbContext.AppUsers
            .FirstOrDefaultAsync(x => x.Id == appUserId, cancellationToken);

        if (user is null)
            throw new InvalidOperationException("User not found.");

        var hasDiscord = user.DiscordUserId != 0 && !string.IsNullOrWhiteSpace(user.DiscordAccessToken);
        var hasTwitch = user.TwitchUserId.HasValue && !string.IsNullOrWhiteSpace(user.TwitchAccessToken);

        var displayName = ResolveDisplayName(user);

        return new LinkedAccountsResponse(
            HasDiscord: hasDiscord,
            HasTwitch: hasTwitch,
            DiscordUsername: hasDiscord ? user.Username : null,
            DiscordDisplayName: hasDiscord ? user.DisplayName : null,
            DiscordUserId: hasDiscord ? user.DiscordUserId : null,
            TwitchUsername: hasTwitch ? user.TwitchUsername : null,
            TwitchDisplayName: hasTwitch ? user.TwitchDisplayName : null,
            TwitchUserId: hasTwitch ? user.TwitchUserId : null,
            ActiveDisplayNameProvider: user.ActiveDisplayNameProvider,
            DisplayName: displayName,
            LinkAccountsWarningDismissed: user.LinkAccountsWarningDismissed
        );
    }

    public async Task DismissLinkAccountsWarningAsync(long appUserId, CancellationToken cancellationToken = default) {
        var user = await dbContext.AppUsers
            .FirstOrDefaultAsync(x => x.Id == appUserId, cancellationToken);

        if (user is null)
            throw new InvalidOperationException("User not found.");

        user.LinkAccountsWarningDismissed = true;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateDisplayNameProviderAsync(long appUserId, string provider, CancellationToken cancellationToken = default) {
        var user = await dbContext.AppUsers
            .FirstOrDefaultAsync(x => x.Id == appUserId, cancellationToken);

        if (user is null)
            throw new InvalidOperationException("User not found.");

        var normalized = provider.ToLowerInvariant();
        if (normalized != "discord" && normalized != "twitch")
            throw new ArgumentException("Provider must be 'discord' or 'twitch'.");

        if (normalized == "discord" && (user.DiscordUserId == 0 || string.IsNullOrWhiteSpace(user.DiscordAccessToken)))
            throw new InvalidOperationException("Discord account is not linked.");

        if (normalized == "twitch" && (!user.TwitchUserId.HasValue || string.IsNullOrWhiteSpace(user.TwitchAccessToken)))
            throw new InvalidOperationException("Twitch account is not linked.");

        user.ActiveDisplayNameProvider = normalized;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<UnlinkAccountWarningResponse> CheckUnlinkDiscordAsync(long appUserId, CancellationToken cancellationToken = default) {
        var user = await dbContext.AppUsers
            .FirstOrDefaultAsync(x => x.Id == appUserId, cancellationToken);

        if (user is null)
            throw new InvalidOperationException("User not found.");

        if (user.DiscordUserId == 0 || string.IsNullOrWhiteSpace(user.DiscordAccessToken))
            throw new InvalidOperationException("Discord account is not linked.");

        var ownedGuilds = await dbContext.GuildInstances
            .Where(x => x.OwnerUserId == appUserId && x.IsActive)
            .ToListAsync(cancellationToken);

        var orphanGuilds = new List<OrphanGuildWarning>();

        foreach (var guild in ownedGuilds) {
            var hasOtherMods = await dbContext.GuildManagers
                .AnyAsync(x => x.GuildInstanceId == guild.Id && x.UserId != appUserId, cancellationToken);

            if (!hasOtherMods) {
                orphanGuilds.Add(new OrphanGuildWarning(guild.DiscordGuildId, guild.Name));
            }
        }

        return new UnlinkAccountWarningResponse(
            HasOrphanGuilds: orphanGuilds.Count > 0,
            OrphanGuilds: orphanGuilds
        );
    }

    public async Task UnlinkDiscordAsync(long appUserId, bool leaveOrphanGuilds, CancellationToken cancellationToken = default) {
        var user = await dbContext.AppUsers
            .FirstOrDefaultAsync(x => x.Id == appUserId, cancellationToken);

        if (user is null)
            throw new InvalidOperationException("User not found.");

        if (user.DiscordUserId == 0 || string.IsNullOrWhiteSpace(user.DiscordAccessToken))
            throw new InvalidOperationException("Discord account is not linked.");

        if (leaveOrphanGuilds) {
            var ownedGuilds = await dbContext.GuildInstances
                .Where(x => x.OwnerUserId == appUserId && x.IsActive)
                .ToListAsync(cancellationToken);

            foreach (var guild in ownedGuilds) {
                var hasOtherMods = await dbContext.GuildManagers
                    .AnyAsync(x => x.GuildInstanceId == guild.Id && x.UserId != appUserId, cancellationToken);

                if (!hasOtherMods) {
                    var discordGuild = discordClient.GetGuild((ulong)guild.DiscordGuildId);
                    if (discordGuild is not null)
                        await discordGuild.LeaveAsync();

                    guild.IsActive = false;
                    guild.UpdatedAtUtc = DateTime.UtcNow;
                }
            }
        }

        user.DiscordUserId = 0;
        user.DiscordAccessToken = null;
        user.DiscordRefreshToken = null;
        user.DiscordTokenExpiresAtUtc = null;

        if (user.ActiveDisplayNameProvider == "discord") {
            user.ActiveDisplayNameProvider = user.TwitchUserId.HasValue ? "twitch" : null;
        }

        if (string.IsNullOrWhiteSpace(user.Username) && user.TwitchUsername is not null) {
            user.Username = user.TwitchUsername;
            user.DisplayName = user.TwitchDisplayName;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UnlinkTwitchAsync(long appUserId, CancellationToken cancellationToken = default) {
        var user = await dbContext.AppUsers
            .FirstOrDefaultAsync(x => x.Id == appUserId, cancellationToken);

        if (user is null)
            throw new InvalidOperationException("User not found.");

        if (!user.TwitchUserId.HasValue || string.IsNullOrWhiteSpace(user.TwitchAccessToken))
            throw new InvalidOperationException("Twitch account is not linked.");

        user.TwitchUserId = null;
        user.TwitchUsername = null;
        user.TwitchDisplayName = null;
        user.TwitchAccessToken = null;
        user.TwitchRefreshToken = null;
        user.TwitchTokenExpiresAtUtc = null;

        if (user.ActiveDisplayNameProvider == "twitch") {
            user.ActiveDisplayNameProvider = user.DiscordUserId != 0 ? "discord" : null;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<AppUser> MergeAccountsAsync(AppUser primaryUser, AppUser duplicateUser, CancellationToken cancellationToken = default) {
        if (primaryUser.Id == duplicateUser.Id)
            return primaryUser;

        var ownedGuilds = await dbContext.GuildInstances
            .Where(x => x.OwnerUserId == duplicateUser.Id)
            .ToListAsync(cancellationToken);
        foreach (var guild in ownedGuilds)
            guild.OwnerUserId = primaryUser.Id;

        var managedGuilds = await dbContext.GuildManagers
            .Where(x => x.UserId == duplicateUser.Id)
            .ToListAsync(cancellationToken);
        foreach (var mgr in managedGuilds) {
            var alreadyManager = await dbContext.GuildManagers
                .AnyAsync(x => x.GuildInstanceId == mgr.GuildInstanceId && x.UserId == primaryUser.Id, cancellationToken);
            if (alreadyManager) {
                dbContext.GuildManagers.Remove(mgr);
            } else {
                mgr.UserId = primaryUser.Id;
            }
        }

        if (primaryUser.DiscordUserId == 0 || string.IsNullOrWhiteSpace(primaryUser.DiscordAccessToken)) {
            primaryUser.DiscordUserId = duplicateUser.DiscordUserId;
            primaryUser.DiscordAccessToken = duplicateUser.DiscordAccessToken;
            primaryUser.DiscordRefreshToken = duplicateUser.DiscordRefreshToken;
            primaryUser.DiscordTokenExpiresAtUtc = duplicateUser.DiscordTokenExpiresAtUtc;
            if (!string.IsNullOrWhiteSpace(duplicateUser.Username))
                primaryUser.Username = duplicateUser.Username;
            if (!string.IsNullOrWhiteSpace(duplicateUser.DisplayName))
                primaryUser.DisplayName = duplicateUser.DisplayName;
        }

        if (!primaryUser.TwitchUserId.HasValue || string.IsNullOrWhiteSpace(primaryUser.TwitchAccessToken)) {
            primaryUser.TwitchUserId = duplicateUser.TwitchUserId;
            primaryUser.TwitchUsername = duplicateUser.TwitchUsername;
            primaryUser.TwitchDisplayName = duplicateUser.TwitchDisplayName;
            primaryUser.TwitchAccessToken = duplicateUser.TwitchAccessToken;
            primaryUser.TwitchRefreshToken = duplicateUser.TwitchRefreshToken;
            primaryUser.TwitchTokenExpiresAtUtc = duplicateUser.TwitchTokenExpiresAtUtc;
        }

        if (string.IsNullOrWhiteSpace(primaryUser.ActiveDisplayNameProvider)) {
            if (primaryUser.DiscordUserId != 0 && !string.IsNullOrWhiteSpace(primaryUser.DiscordAccessToken))
                primaryUser.ActiveDisplayNameProvider = "discord";
            else if (primaryUser.TwitchUserId.HasValue)
                primaryUser.ActiveDisplayNameProvider = "twitch";
        }

        dbContext.AppUsers.Remove(duplicateUser);
        await dbContext.SaveChangesAsync(cancellationToken);

        return primaryUser;
    }

    public static string ResolveDisplayName(AppUser user) {
        var provider = user.ActiveDisplayNameProvider?.ToLowerInvariant();

        if (provider == "twitch" && user.TwitchDisplayName is not null)
            return user.TwitchDisplayName;

        if (provider == "discord" && user.DisplayName is not null)
            return user.DisplayName;

        if (!string.IsNullOrWhiteSpace(user.DisplayName))
            return user.DisplayName;

        if (!string.IsNullOrWhiteSpace(user.TwitchDisplayName))
            return user.TwitchDisplayName;

        return user.Username;
    }
}
