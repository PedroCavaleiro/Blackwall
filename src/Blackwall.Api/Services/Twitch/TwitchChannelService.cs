using Blackwall.Core.DTOs;
using Blackwall.Core.Entities;
using Blackwall.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Blackwall.Api.Services.Twitch;

public sealed class TwitchChannelService(BlackwallDbContext dbContext) {

    public async Task ClaimChannelOwnershipAsync(
        long appUserId,
        TwitchUserDto twitchUser,
        CancellationToken cancellationToken = default
    ) {
        var twitchUserId = long.Parse(twitchUser.Id);

        var existing = await dbContext.TwitchChannelInstances
            .FirstOrDefaultAsync(x => x.TwitchUserId == twitchUserId, cancellationToken);

        if (existing is null)
            return;

        existing.Username = twitchUser.Login;
        existing.DisplayName = twitchUser.DisplayName;
        existing.ProfileImageUrl = twitchUser.ProfileImageUrl;
        existing.UpdatedAtUtc = DateTime.UtcNow;

        if (existing.OwnerUserId is null) {
            existing.OwnerUserId = appUserId;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<TwitchChannelInstance> CreateOrUpdateChannelInstanceAsync(
        long appUserId,
        TwitchUserDto twitchUser,
        CancellationToken cancellationToken = default
    ) {
        var twitchUserId = long.Parse(twitchUser.Id);

        var existing = await dbContext.TwitchChannelInstances
            .FirstOrDefaultAsync(x => x.TwitchUserId == twitchUserId, cancellationToken);

        if (existing is not null) {
            existing.Username = twitchUser.Login;
            existing.DisplayName = twitchUser.DisplayName;
            existing.ProfileImageUrl = twitchUser.ProfileImageUrl;
            existing.IsActive = true;
            existing.UpdatedAtUtc = DateTime.UtcNow;
            if (existing.OwnerUserId is null)
                existing.OwnerUserId = appUserId;

            await dbContext.SaveChangesAsync(cancellationToken);
            return existing;
        }

        var instance = new TwitchChannelInstance {
            TwitchUserId = twitchUserId,
            Username = twitchUser.Login,
            DisplayName = twitchUser.DisplayName,
            ProfileImageUrl = twitchUser.ProfileImageUrl,
            IsActive = true,
            OwnerUserId = appUserId,
            UpdatedAtUtc = DateTime.UtcNow
        };

        dbContext.TwitchChannelInstances.Add(instance);
        await dbContext.SaveChangesAsync(cancellationToken);
        return instance;
    }

    public async Task<IReadOnlyList<ManageableTwitchChannelResponse>> GetManageableChannelsAsync(
        long appUserId,
        CancellationToken cancellationToken = default
    ) {
        var user = await dbContext.AppUsers
            .FirstOrDefaultAsync(x => x.Id == appUserId, cancellationToken);

        if (user is null || !user.TwitchUserId.HasValue)
            return [];

        var twitchUserId = user.TwitchUserId.Value;

        var ownChannel = await dbContext.TwitchChannelInstances
            .Where(x => x.TwitchUserId == twitchUserId)
            .Select(x => new {
                x.Id,
                x.TwitchUserId,
                x.Username,
                x.DisplayName,
                x.ProfileImageUrl,
                x.IsActive,
                x.OwnerUserId
            })
            .FirstOrDefaultAsync(cancellationToken);

        var managedChannelIds = await dbContext.TwitchChannelManagers
            .Where(x => x.UserId == appUserId)
            .Select(x => x.TwitchChannelInstanceId)
            .ToListAsync(cancellationToken);

        var managedChannels = await dbContext.TwitchChannelInstances
            .Where(x => managedChannelIds.Contains(x.Id))
            .Select(x => new {
                x.Id,
                x.TwitchUserId,
                x.Username,
                x.DisplayName,
                x.ProfileImageUrl,
                x.IsActive,
                x.OwnerUserId
            })
            .ToListAsync(cancellationToken);

        var results = new List<ManageableTwitchChannelResponse>();

        if (ownChannel is not null) {
            results.Add(new ManageableTwitchChannelResponse(
                ownChannel.TwitchUserId,
                ownChannel.Username,
                ownChannel.DisplayName,
                ownChannel.ProfileImageUrl,
                IsOwner: true,
                BotInstalled: ownChannel.IsActive,
                CanOpen: ownChannel.IsActive
            ));
        } else {
            results.Add(new ManageableTwitchChannelResponse(
                twitchUserId,
                user.TwitchUsername ?? "",
                user.TwitchDisplayName ?? user.TwitchUsername ?? "",
                user.TwitchProfileImageUrl,
                IsOwner: true,
                BotInstalled: false,
                CanOpen: false
            ));
        }

        foreach (var ch in managedChannels) {
            if (ch.TwitchUserId == twitchUserId)
                continue;

            results.Add(new ManageableTwitchChannelResponse(
                ch.TwitchUserId,
                ch.Username,
                ch.DisplayName,
                ch.ProfileImageUrl,
                IsOwner: false,
                BotInstalled: ch.IsActive,
                CanOpen: ch.IsActive
            ));
        }

        return results;
    }

    public async Task<bool> CanOpenChannelAsync(
        long appUserId,
        long twitchUserId,
        CancellationToken cancellationToken = default
    ) {
        var instance = await dbContext.TwitchChannelInstances
            .Where(x => x.TwitchUserId == twitchUserId && x.IsActive)
            .Select(x => new { x.Id, x.OwnerUserId })
            .FirstOrDefaultAsync(cancellationToken);

        if (instance is null)
            return false;

        if (instance.OwnerUserId == appUserId)
            return true;

        return await dbContext.TwitchChannelManagers
            .AnyAsync(x => x.TwitchChannelInstanceId == instance.Id && x.UserId == appUserId, cancellationToken);
    }
}
