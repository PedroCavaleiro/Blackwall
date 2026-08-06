using Blackwall.Core.Configuration;
using Blackwall.Core.DTOs;
using Blackwall.Core.Entities;
using Blackwall.Core.Services;
using Blackwall.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Blackwall.Api.Services.Twitch;

public sealed class TwitchChannelService(
    BlackwallDbContext dbContext,
    IOptions<TwitchOptions> twitchOptions,
    IOptions<AppConfiguration> appConfiguration
) {
    private static readonly HttpClient ModerationHttp = new();
    private readonly TwitchOptions _twitchOptions = twitchOptions.Value;
    private readonly AppConfiguration _appConfig = appConfiguration.Value;

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

        existing.OwnerUserId ??= appUserId;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task CreateOrUpdateChannelInstanceAsync(
        long appUserId,
        TwitchUserDto twitchUser,
        string? encryptedAccessToken = null,
        string? encryptedRefreshToken = null,
        DateTime? tokenExpiresAtUtc = null,
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
            existing.OwnerUserId ??= appUserId;
            if (encryptedAccessToken is not null) {
                existing.BotAccessToken = encryptedAccessToken;
                existing.BotRefreshToken = encryptedRefreshToken;
                existing.BotTokenExpiresAtUtc = tokenExpiresAtUtc;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var instance = new TwitchChannelInstance {
            TwitchUserId = twitchUserId,
            Username = twitchUser.Login,
            DisplayName = twitchUser.DisplayName,
            ProfileImageUrl = twitchUser.ProfileImageUrl,
            IsActive = true,
            OwnerUserId = appUserId,
            UpdatedAtUtc = DateTime.UtcNow,
            BotAccessToken = encryptedAccessToken,
            BotRefreshToken = encryptedRefreshToken,
            BotTokenExpiresAtUtc = tokenExpiresAtUtc
        };

        dbContext.TwitchChannelInstances.Add(instance);
        await dbContext.SaveChangesAsync(cancellationToken);
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

    public async Task AutoAddManagersAsync(long appUserId, string accessToken, CancellationToken cancellationToken = default) {
        var user = await dbContext.AppUsers
            .FirstOrDefaultAsync(x => x.Id == appUserId, cancellationToken);

        if (user is null || !user.TwitchUserId.HasValue)
            return;

        var twitchUserId = user.TwitchUserId.Value.ToString();

        List<long> moderatedChannelIds;
        try {
            moderatedChannelIds = await GetModeratedChannelIdsAsync(accessToken, twitchUserId, cancellationToken);
        } catch {
            return;
        }

        if (moderatedChannelIds.Count == 0)
            return;

        var channelsWithAutoAdd = await dbContext.TwitchChannelInstances
            .Include(c => c.Configuration)
            .Where(c => moderatedChannelIds.Contains(c.TwitchUserId) && c.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var channel in channelsWithAutoAdd) {
            if (channel.Configuration is null || !channel.Configuration.AutoAddManagers)
                continue;

            if (channel.OwnerUserId == appUserId)
                continue;

            var alreadyManager = await dbContext.TwitchChannelManagers
                .AnyAsync(m => m.TwitchChannelInstanceId == channel.Id && m.UserId == appUserId, cancellationToken);

            if (alreadyManager)
                continue;

            var wasRemoved = await dbContext.TwitchRemovedManagers
                .AnyAsync(r => r.TwitchChannelInstanceId == channel.Id && r.UserId == appUserId, cancellationToken);

            if (wasRemoved)
                continue;

            dbContext.TwitchChannelManagers.Add(new TwitchChannelManager {
                TwitchChannelInstanceId = channel.Id,
                UserId = appUserId,
                IsAdmin = false
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task AutoAddUserToExistingChannelsAsync(long appUserId, CancellationToken cancellationToken = default) {
        var user = await dbContext.AppUsers
            .FirstOrDefaultAsync(x => x.Id == appUserId, cancellationToken);

        if (user is null || !user.TwitchUserId.HasValue)
            return;

        var twitchUserId = user.TwitchUserId.Value;

        var channelsWithAutoAdd = await dbContext.TwitchChannelInstances
            .Include(c => c.Configuration)
            .Where(c => c.IsActive && c.OwnerUserId != appUserId)
            .ToListAsync(cancellationToken);

        foreach (var channel in channelsWithAutoAdd) {
            if (channel.Configuration is null || !channel.Configuration.AutoAddManagers)
                continue;

            var key = AesCrypto.GetBytes(_appConfig.EncryptionKey);
            var iv = AesCrypto.GetBytes(_appConfig.EncryptionIv);

            if (string.IsNullOrWhiteSpace(channel.BotAccessToken))
                continue;

            try {
                var channelToken = AesCrypto.DecryptString(channel.BotAccessToken, key, iv);
                var isModerator = await CheckIsModeratorAsync(channelToken, channel.TwitchUserId.ToString(), twitchUserId.ToString(), cancellationToken);

                if (!isModerator)
                    continue;

                var alreadyManager = await dbContext.TwitchChannelManagers
                    .AnyAsync(m => m.TwitchChannelInstanceId == channel.Id && m.UserId == appUserId, cancellationToken);

                if (alreadyManager)
                    continue;

                var wasRemoved = await dbContext.TwitchRemovedManagers
                    .AnyAsync(r => r.TwitchChannelInstanceId == channel.Id && r.UserId == appUserId, cancellationToken);

                if (wasRemoved)
                    continue;

                dbContext.TwitchChannelManagers.Add(new TwitchChannelManager {
                    TwitchChannelInstanceId = channel.Id,
                    UserId = appUserId,
                    IsAdmin = false
                });
            }
            catch {
                // ignored
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<List<long>> GetModeratedChannelIdsAsync(string accessToken, string twitchUserId, CancellationToken cancellationToken) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.twitch.tv/helix/moderation/channels?user_id={twitchUserId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("Client-Id", _twitchOptions.ClientId);

        using var response = await ModerationHttp.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var data = document.RootElement.GetProperty("data");
        var result = new List<long>();

        foreach (var item in data.EnumerateArray()) {
            if (item.TryGetProperty("broadcaster_id", out var idProp) &&
                long.TryParse(idProp.GetString(), out var broadcasterId)) {
                result.Add(broadcasterId);
            }
        }

        return result;
    }

    private async Task<bool> CheckIsModeratorAsync(string channelAccessToken, string broadcasterId, string userId, CancellationToken cancellationToken) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.twitch.tv/helix/moderation/moderators?broadcaster_id={broadcasterId}&user_id={userId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", channelAccessToken);
        request.Headers.Add("Client-Id", _twitchOptions.ClientId);

        using var response = await ModerationHttp.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return false;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var data = document.RootElement.GetProperty("data");
        return data.GetArrayLength() > 0;
    }
}
