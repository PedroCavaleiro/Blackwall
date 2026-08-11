using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Blackwall.Core.Configuration;
using Blackwall.Core.Entities;
using Blackwall.Core.Services;
using Blackwall.Modules.ContentGuard;
using Blackwall.Modules.DetectionMatrix;
using Blackwall.Infrastructure.Persistence;
using Blackwall.Modules.LinkProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwitchLib.Api;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.Api.Helix.Models.Moderation.BanUser;
// ReSharper disable NotAccessedPositionalProperty.Local
// ReSharper disable EventNeverSubscribedTo.Global
// ReSharper disable RedundantAssignment
// ReSharper disable AllUnderscoreLocalParameterName
// ReSharper disable NullableWarningSuppressionIsUsed
// ReSharper disable MethodHasAsyncOverload
// ReSharper disable NotAccessedPositionalProperty.Global
// ReSharper disable PartialTypeWithSinglePart

namespace Blackwall.Bot.Twitch;

public sealed partial class TwitchBotService(
    BlackwallDbContext dbContext,
    IOptions<TwitchOptions> twitchOptions,
    IOptions<AppConfiguration> appConfig,
    [FromKeyedServices("twitch")] DetectionService spamDetectionService,
    [FromKeyedServices("twitch")] LinkProtectionService linkProtectionService,
    ContentGuardService contentGuardService,
    ISafeBrowsingService safeBrowsingService,
    ILogger<TwitchBotService> logger
)
    : IAsyncDisposable {
    private readonly TwitchOptions _twitchOptions = twitchOptions.Value;
    private readonly AppConfiguration _appConfig = appConfig.Value;

    private TwitchClient? _client;
    private TwitchAPI? _botApi;
    private string? _botUserId;
    private string? _botAccessToken;
    private readonly ConcurrentDictionary<string, long> _channelNameToUserId = new();
    private byte[]? _encKey;
    private byte[]? _encIv;

    private readonly ConcurrentDictionary<long, ChannelRecord> _channels = new();
    private readonly HttpClient _tokenHttp = new();
    private Timer? _tokenRefreshTimer;
    private Timer? _channelTokenRefreshTimer;
    private static readonly string TokenFilePath = Path.Combine(AppContext.BaseDirectory, "twitch-bot-tokens.json");

    public event EventHandler<OnMessageReceivedArgs>? OnMessageReceived;

    private record ChannelRecord(long TwitchUserId, string Username, string AccessToken, string CommandTrigger, bool IsEnabled, bool IsDryRun, TwitchDetectionConfig? DetectionConfig, TwitchLinkConfig? LinkConfig, TwitchContentGuardConfig? ContentGuardConfig);

    public record TwitchDetectionConfig(
        int MaxMessagesPerWindow,
        int RateLimitWindowSeconds,
        int DuplicateMessageThreshold,
        int DuplicateWindowSeconds,
        int MentionLimit,
        InfractionAction RateLimitAction,
        int RateLimitTimeoutMinutes,
        InfractionAction DuplicateAction,
        int DuplicateTimeoutMinutes,
        InfractionAction MentionLimitAction,
        int MentionLimitTimeoutMinutes
    );

    public record TwitchLinkConfig(
        bool BlockSuspiciousLinks,
        bool LinkWhitelistMode,
        bool SafeBrowsingEnabled,
        bool SafeBrowsingBlockUnsure,
        InfractionAction SuspiciousLinkAction,
        int SuspiciousLinkTimeoutMinutes
    );

    public record TwitchContentGuardConfig(
        bool IsEnabled,
        bool FuzzyMatching,
        int FuzzyThreshold,
        InfractionAction Action,
        int TimeoutMinutes
    );

    private record TwitchTokenResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string AccessToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("refresh_token")] string RefreshToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("expires_in")] int ExpiresIn
    );

    private record PersistedBotTokens(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string AccessToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("refresh_token")] string RefreshToken
    );

    public async Task InitializeAsync(CancellationToken cancellationToken = default) {
        _encKey = AesCrypto.GetBytes(_appConfig.EncryptionKey);
        _encIv = AesCrypto.GetBytes(_appConfig.EncryptionIv);

        var channels = await LoadActiveChannelsAsync(cancellationToken);

        if (channels.Count == 0) {
            logger.LogInformation("No active Twitch channels found — bot will connect when channels are added");
            return;
        }

        var firstChannel = channels[0];

        string ircUsername;
        string ircToken;

        if (!string.IsNullOrWhiteSpace(_twitchOptions.BotUsername) && !string.IsNullOrWhiteSpace(_twitchOptions.BotAccessToken)) {
            ircUsername = _twitchOptions.BotUsername;

            var (persistedAccess, persistedRefresh) = LoadPersistedTokens();
            var refreshToken = persistedRefresh ?? _twitchOptions.BotRefreshToken;

            if (!string.IsNullOrWhiteSpace(refreshToken)) {
                _twitchOptions.BotRefreshToken = refreshToken;
                _botAccessToken = await RefreshBotTokenAsync();
                logger.LogInformation("Refreshed bot access token via refresh token");
            } else {
                _botAccessToken = persistedAccess
                    ?? (_twitchOptions.BotAccessToken.StartsWith("oauth:")
                        ? _twitchOptions.BotAccessToken["oauth:".Length..]
                        : _twitchOptions.BotAccessToken);
                logger.LogWarning("No bot refresh token configured — using static access token. It will expire and the bot will disconnect. Set TWITCH__BOT_REFRESH_TOKEN for auto-refresh");
            }

            ircToken = $"oauth:{_botAccessToken}";
            logger.LogInformation("Connecting to IRC as dedicated bot account: {BotUser}", ircUsername);

            _botApi = new TwitchAPI {
                Settings = {
                    ClientId = _twitchOptions.ClientId,
                    AccessToken = _botAccessToken
                }
            };

            var botUserResponse = await _botApi.Helix.Users.GetUsersAsync(logins: [_twitchOptions.BotUsername]);
            _botUserId = botUserResponse.Users[0].Id;
            logger.LogInformation("Bot account user ID: {BotUserId}", _botUserId);

            if (!string.IsNullOrWhiteSpace(_twitchOptions.BotRefreshToken)) {
                _tokenRefreshTimer = new Timer(_ => _ = RefreshBotTokenPeriodicAsync(), null, TimeSpan.FromHours(3), TimeSpan.FromHours(3));
            }

            _channelTokenRefreshTimer = new Timer(_ => _ = RefreshChannelTokensPeriodicAsync(), null, TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30));
        } else {
            ircUsername = firstChannel.Username;
            ircToken = $"oauth:{DecryptToken(firstChannel.BotAccessToken!)}";
            logger.LogWarning("No dedicated bot account configured — connecting as channel owner {Owner}. Set TWITCH__BOT_USERNAME and TWITCH__BOT_ACCESS_TOKEN", ircUsername);

            var fallbackToken = DecryptToken(firstChannel.BotAccessToken!);
            _botApi = new TwitchAPI {
                Settings = {
                    ClientId = _twitchOptions.ClientId,
                    AccessToken = fallbackToken
                }
            };
            _botUserId = firstChannel.TwitchUserId.ToString();
        }

        var credentials = new ConnectionCredentials(ircUsername, ircToken);
        _client = new TwitchClient();
        _client.Initialize(credentials, firstChannel.Username);

        _client.OnConnected += OnConnected;
        _client.OnJoinedChannel += OnJoinedChannel;
        _client.OnMessageReceived += OnMessageReceivedInternal;
        _client.OnDisconnected += OnDisconnected;

        foreach (var ch in channels) {
            var decryptedToken = DecryptToken(ch.BotAccessToken!);
            _channels[ch.TwitchUserId] = new ChannelRecord(ch.TwitchUserId, ch.Username, decryptedToken, GetCommandTrigger(ch), GetIsEnabled(ch), GetIsDryRun(ch), GetDetectionConfig(ch), GetLinkConfig(ch), GetContentGuardConfig(ch));
            _channelNameToUserId[ch.Username.ToLowerInvariant()] = ch.TwitchUserId;
        }

        await _client.ConnectAsync();

        foreach (var ch in channels) {
            if (ch.TwitchUserId != firstChannel.TwitchUserId) {
                await _client.JoinChannelAsync(ch.Username);
            }
        }

        logger.LogInformation("TwitchBot connected to {Count} channel(s)", channels.Count);
    }

    public async Task RefreshChannelsAsync(CancellationToken cancellationToken = default) {
        if (_client is null || !_client.IsConnected) {
            await InitializeAsync(cancellationToken);
            return;
        }

        var dbChannels = await LoadActiveChannelsAsync(cancellationToken);
        var dbChannelIds = dbChannels.Select(c => c.TwitchUserId).ToHashSet();
        var currentIds = _channels.Keys.ToHashSet();

        var toJoin = dbChannels.Where(c => !currentIds.Contains(c.TwitchUserId)).ToList();
        var toLeave = currentIds.Except(dbChannelIds).ToList();

        foreach (var ch in toJoin) {
            var decryptedToken = DecryptToken(ch.BotAccessToken!);
            _channels[ch.TwitchUserId] = new ChannelRecord(ch.TwitchUserId, ch.Username, decryptedToken, GetCommandTrigger(ch), GetIsEnabled(ch), GetIsDryRun(ch), GetDetectionConfig(ch), GetLinkConfig(ch), GetContentGuardConfig(ch));
            _channelNameToUserId[ch.Username.ToLowerInvariant()] = ch.TwitchUserId;

            await _client.JoinChannelAsync(ch.Username);
            logger.LogInformation("Joined Twitch channel: {Channel}", ch.Username);
        }

        foreach (var id in toLeave) {
            if (_channels.TryGetValue(id, out var record)) {
                await _client.LeaveChannelAsync(record.Username);
                _channels.TryRemove(id, out _);
                _channelNameToUserId.TryRemove(record.Username.ToLowerInvariant(), out _);
                logger.LogInformation("Left Twitch channel: {Channel}", record.Username);
            }
        }
    }

    public async Task SendMessageAsync(string channelUsername, string message) {
        if (_client is null || !_client.IsConnected) {
            logger.LogWarning("Cannot send message — Twitch client not connected");
            return;
        }

        await _client.SendMessageAsync(channelUsername, message);
    }

    public async Task DeleteMessageAsync(long broadcasterId, string messageId) {
        if (_botApi is null || _botUserId is null) {
            logger.LogWarning("No bot API instance available");
            return;
        }

        await _botApi.Helix.Moderation.DeleteChatMessagesAsync(broadcasterId.ToString(), messageId);
        logger.LogInformation("Deleted message {MessageId} in channel {BroadcasterId}", messageId, broadcasterId);
    }

    public async Task TimeoutUserAsync(long broadcasterId, long userId, int durationSeconds, string? reason = null) {
        if (_botApi is null || _botUserId is null) {
            logger.LogWarning("No bot API instance available");
            return;
        }

        await _botApi.Helix.Moderation.BanUserAsync(
            broadcasterId.ToString(),
            _botUserId,
            new BanUserRequest {
                UserId = userId.ToString(),
                Reason = reason ?? "Timed out by moderator",
                Duration = durationSeconds
            }
        );
        logger.LogInformation("Timed out user {UserId} in channel {BroadcasterId} for {Duration}s", userId, broadcasterId, durationSeconds);
    }

    public async Task BanUserAsync(long broadcasterId, long userId, string? reason = null) {
        if (_botApi is null || _botUserId is null) {
            logger.LogWarning("No bot API instance available");
            return;
        }

        await _botApi.Helix.Moderation.BanUserAsync(
            broadcasterId.ToString(),
            _botUserId,
            new BanUserRequest {
                UserId = userId.ToString(),
                Reason = reason ?? "Banned by moderator"
            }
        );
        logger.LogInformation("Banned user {UserId} in channel {BroadcasterId}", userId, broadcasterId);
    }

    public async Task UnbanUserAsync(long broadcasterId, long userId) {
        if (_botApi is null || _botUserId is null) {
            logger.LogWarning("No bot API instance available");
            return;
        }

        await _botApi.Helix.Moderation.UnbanUserAsync(broadcasterId.ToString(), _botUserId, userId.ToString());
        logger.LogInformation("Unbanned user {UserId} in channel {BroadcasterId}", userId, broadcasterId);
    }

    public async Task<List<(long UserId, string? Username, string? Reason, DateTime? BannedAtUtc)>> GetChannelBansAsync(long broadcasterId, CancellationToken cancellationToken = default) {
        string? accessToken = null;

        if (_channels.TryGetValue(broadcasterId, out var channel))
            accessToken = channel.AccessToken;

        if (string.IsNullOrWhiteSpace(accessToken)) {
            if (_botApi is null || _botUserId is null) {
                logger.LogWarning("No bot API instance available and no channel token for {BroadcasterId}", broadcasterId);
                return [];
            }
            accessToken = _botAccessToken;
        }

        if (string.IsNullOrWhiteSpace(accessToken)) {
            logger.LogWarning("No access token available for channel {BroadcasterId}", broadcasterId);
            return [];
        }

        var api = new TwitchAPI {
            Settings = {
                ClientId = _twitchOptions.ClientId,
                AccessToken = accessToken
            }
        };

        var allBans = new List<(long, string?, string?, DateTime?)>();
        string? cursor = null;

        do {
            var result = string.IsNullOrEmpty(cursor)
                ? await api.Helix.Moderation.GetBannedUsersAsync(broadcasterId.ToString(), first: 100)
                : await api.Helix.Moderation.GetBannedUsersAsync(broadcasterId.ToString(), first: 100, after: cursor);

            foreach (var ban in result.Data) {
                if (ban.ExpiresAt.HasValue)
                    continue;

                DateTime? bannedAt = null;
                if (DateTime.TryParse(ban.CreatedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
                    bannedAt = parsed;

                allBans.Add((long.Parse(ban.UserId), ban.UserName, ban.Reason, bannedAt));
            }

            cursor = result.Pagination?.Cursor;
        } while (!string.IsNullOrEmpty(cursor));

        logger.LogInformation("Retrieved {Count} banned users for channel {BroadcasterId}", allBans.Count, broadcasterId);
        return allBans;
    }

    private Task OnConnected(object? sender, EventArgs e) {
        logger.LogInformation("TwitchBot connected to IRC");
        return Task.CompletedTask;
    }

    private Task OnJoinedChannel(object? sender, OnJoinedChannelArgs e) {
        logger.LogInformation("Joined Twitch channel: {Channel}", e.Channel);
        return Task.CompletedTask;
    }

    private async Task OnMessageReceivedInternal(object? sender, OnMessageReceivedArgs e) {
        OnMessageReceived?.Invoke(sender, e);

        var channelName = e.ChatMessage.Channel.ToLowerInvariant();
        if (!_channelNameToUserId.TryGetValue(channelName, out var broadcasterId))
            return;

        if (!_channels.TryGetValue(broadcasterId, out var record) || !record.IsEnabled)
            return;

        if (e.ChatMessage.UserId == _botUserId)
            return;

        var detectionConfig = record.DetectionConfig;
        if (detectionConfig is not null) {
            var twitchUserId = long.Parse(e.ChatMessage.UserId);
            var violations = new List<(string Type, InfractionAction Action, int TimeoutMinutes)>(3);

            if (detectionConfig is { MaxMessagesPerWindow: > 0, RateLimitWindowSeconds: > 0 }) {
                if (await spamDetectionService.IsRateLimitedAsync(
                        broadcasterId.ToString(), twitchUserId.ToString(), e.ChatMessage.Id,
                        detectionConfig.MaxMessagesPerWindow, detectionConfig.RateLimitWindowSeconds)) {
                    violations.Add(("rate_limit", detectionConfig.RateLimitAction, detectionConfig.RateLimitTimeoutMinutes));
                }
            }

            if (detectionConfig.DuplicateMessageThreshold > 0) {
                var dupResult = await spamDetectionService.IsDuplicateAsync(
                        broadcasterId.ToString(), twitchUserId.ToString(), e.ChatMessage.Id,
                        e.ChatMessage.Message,
                        detectionConfig.DuplicateMessageThreshold, detectionConfig.DuplicateWindowSeconds);
                if (dupResult.IsDuplicate) {
                    violations.Add(("duplicate", detectionConfig.DuplicateAction, detectionConfig.DuplicateTimeoutMinutes));
                }
            }

            if (detectionConfig.MentionLimit > 0) {
                if (DetectionService.ExceedsMentionLimit(e.ChatMessage.Message, detectionConfig.MentionLimit)) {
                    violations.Add(("mention_limit", detectionConfig.MentionLimitAction, detectionConfig.MentionLimitTimeoutMinutes));
                }
            }

            if (violations.Count > 0) {
                var violationSummary = string.Join(", ", violations.Select(v => v.Type));
                var (effectiveAction, effectiveTimeout) = GetMostSevereViolation(violations);

                logger.LogInformation(
                    "Spam detected in channel {BroadcasterId} from user {UserId}: {Violations} (DryRun={DryRun}, Action={Action})",
                    broadcasterId, twitchUserId, violationSummary, record.IsDryRun, effectiveAction);

                if (!record.IsDryRun) {
                    try {
                        await DeleteMessageAsync(broadcasterId, e.ChatMessage.Id);
                    } catch (Exception ex) {
                        logger.LogWarning(ex,
                            "Failed to delete spam message {MessageId} in channel {BroadcasterId}",
                            e.ChatMessage.Id, broadcasterId);
                    }

                    await ApplyDetectionActionAsync(broadcasterId, twitchUserId, effectiveAction, effectiveTimeout);
                }

                return;
            }
        }

        var linkConfig = record.LinkConfig;
        if (linkConfig is not null && linkConfig.BlockSuspiciousLinks) {
            var twitchUserId2 = long.Parse(e.ChatMessage.UserId);
            var linkBlocked = false;
            var linkViolation = "suspicious_link";

            var urls = LinkProtectionService.ExtractUrls(e.ChatMessage.Message);
            if (urls.Count > 0) {
                foreach (var url in urls) {
                    if (await linkProtectionService.IsLinkBlockedAsync(broadcasterId, url)) {
                        linkBlocked = true;
                        break;
                    }
                }

                if (!linkBlocked && linkConfig.SafeBrowsingEnabled) {
                    foreach (var url in urls) {
                        var sbResult = await safeBrowsingService.CheckUrlAsync(url);
                        if (sbResult == SafeBrowsingResult.Unsafe
                            || (sbResult == SafeBrowsingResult.Unsure && linkConfig.SafeBrowsingBlockUnsure)) {
                            linkBlocked = true;
                            linkViolation = "safe_browsing";
                            break;
                        }
                    }
                }
            }

            if (linkBlocked) {
                logger.LogInformation(
                    "Link violation in channel {BroadcasterId} from user {UserId}: {Violation} (DryRun={DryRun}, Action={Action})",
                    broadcasterId, twitchUserId2, linkViolation, record.IsDryRun, linkConfig.SuspiciousLinkAction);

                if (!record.IsDryRun) {
                    try {
                        await DeleteMessageAsync(broadcasterId, e.ChatMessage.Id);
                    } catch (Exception ex) {
                        logger.LogWarning(ex,
                            "Failed to delete link violation message {MessageId} in channel {BroadcasterId}",
                            e.ChatMessage.Id, broadcasterId);
                    }

                    await ApplyDetectionActionAsync(broadcasterId, twitchUserId2, linkConfig.SuspiciousLinkAction, linkConfig.SuspiciousLinkTimeoutMinutes);
                }

                return;
            }
        }

        var contentGuardConfig = record.ContentGuardConfig;
        if (contentGuardConfig is not null && contentGuardConfig.IsEnabled) {
            var twitchUserIdCg = long.Parse(e.ChatMessage.UserId);
            var cgViolations = await contentGuardService.EvaluateTwitchAsync(
                e.ChatMessage.Message, broadcasterId,
                contentGuardConfig.FuzzyMatching, contentGuardConfig.FuzzyThreshold);

            if (cgViolations.Count > 0) {
                var violationSummary = string.Join(", ", cgViolations);
                logger.LogInformation(
                    "Content Guard violation in channel {BroadcasterId} from user {UserId}: {Violations} (DryRun={DryRun}, Action={Action})",
                    broadcasterId, twitchUserIdCg, violationSummary, record.IsDryRun, contentGuardConfig.Action);

                if (!record.IsDryRun) {
                    try {
                        await DeleteMessageAsync(broadcasterId, e.ChatMessage.Id);
                    } catch (Exception ex) {
                        logger.LogWarning(ex,
                            "Failed to delete Content Guard message {MessageId} in channel {BroadcasterId}",
                            e.ChatMessage.Id, broadcasterId);
                    }

                    await ApplyDetectionActionAsync(broadcasterId, twitchUserIdCg, contentGuardConfig.Action, contentGuardConfig.TimeoutMinutes);
                }

                return;
            }
        }

        var trigger = record.CommandTrigger;
        if (!e.ChatMessage.Message.StartsWith(trigger, StringComparison.Ordinal))
            return;

        var body = e.ChatMessage.Message[trigger.Length..];
        var parts = body.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return;

        var command = parts[0].ToLowerInvariant();

        if (command == "echo") {
            if (e.ChatMessage.UserId != broadcasterId.ToString())
                return;

            if (record.IsDryRun) {
                logger.LogInformation("[DRY RUN] echo command from owner in #{Channel} — not sending", e.ChatMessage.Channel);
                return;
            }

            await _client!.SendMessageAsync(e.ChatMessage.Channel, "echo");
            logger.LogInformation("echo command executed by owner in #{Channel}", e.ChatMessage.Channel);
        }
    }

    private Task OnDisconnected(object? sender, EventArgs e) {
        logger.LogWarning("TwitchBot disconnected from IRC");
        return Task.CompletedTask;
    }

    private async Task<List<TwitchChannelInstance>> LoadActiveChannelsAsync(CancellationToken cancellationToken) {
        return await dbContext.TwitchChannelInstances
            .Include(c => c.Configuration)
            .Where(c => c.IsActive && c.BotAccessToken != null)
            .ToListAsync(cancellationToken);
    }

    private static string GetCommandTrigger(TwitchChannelInstance ch) =>
        ch.Configuration?.CommandTrigger ?? "!";

    private static bool GetIsEnabled(TwitchChannelInstance ch) =>
        ch.Configuration?.IsEnabled ?? true;

    private static bool GetIsDryRun(TwitchChannelInstance ch) =>
        ch.Configuration?.IsDryRun ?? false;

    private static TwitchDetectionConfig? GetDetectionConfig(TwitchChannelInstance ch) {
        var config = ch.Configuration;
        if (config is null)
            return null;

        if (config.MaxMessagesPerWindow <= 0
            && config.RateLimitWindowSeconds <= 0
            && config.DuplicateMessageThreshold <= 0
            && config.MentionLimit <= 0)
            return null;

        return new TwitchDetectionConfig(
            config.MaxMessagesPerWindow,
            config.RateLimitWindowSeconds,
            config.DuplicateMessageThreshold,
            config.DuplicateWindowSeconds,
            config.MentionLimit,
            config.RateLimitAction,
            config.RateLimitTimeoutMinutes,
            config.DuplicateAction,
            config.DuplicateTimeoutMinutes,
            config.MentionLimitAction,
            config.MentionLimitTimeoutMinutes
        );
    }

    private static TwitchLinkConfig? GetLinkConfig(TwitchChannelInstance ch) {
        var config = ch.Configuration;
        if (config is null || !config.BlockSuspiciousLinks)
            return null;

        return new TwitchLinkConfig(
            config.BlockSuspiciousLinks,
            config.LinkWhitelistMode,
            config.SafeBrowsingEnabled,
            config.SafeBrowsingBlockUnsure,
            config.SuspiciousLinkAction,
            config.SuspiciousLinkTimeoutMinutes
        );
    }

    private static TwitchContentGuardConfig? GetContentGuardConfig(TwitchChannelInstance ch) {
        var config = ch.Configuration;
        if (config is null || !config.IsContentGuardEnabled)
            return null;

        return new TwitchContentGuardConfig(
            config.IsContentGuardEnabled,
            config.ContentGuardFuzzyMatching,
            config.ContentGuardFuzzyThreshold,
            config.ContentGuardAction,
            config.ContentGuardTimeoutMinutes
        );
    }

    private static (InfractionAction Action, int TimeoutMinutes) GetMostSevereViolation(
        List<(string Type, InfractionAction Action, int TimeoutMinutes)> violations
    ) {
        var worst = violations[0];
        foreach (var v in violations) {
            if (v.Action > worst.Action)
                worst = v;
        }
        return (worst.Action, worst.TimeoutMinutes);
    }

    private async Task ApplyDetectionActionAsync(
        long broadcasterId, long twitchUserId, InfractionAction action, int timeoutMinutes
    ) {
        try {
            switch (action) {
                case InfractionAction.Timeout:
                    await TimeoutUserAsync(broadcasterId, twitchUserId, timeoutMinutes * 60);
                    break;
                case InfractionAction.Ban:
                case InfractionAction.SoftBan:
                    await BanUserAsync(broadcasterId, twitchUserId);
                    break;
                case InfractionAction.Kick:
                    await DeleteMessageAsync(broadcasterId, twitchUserId.ToString());
                    break;
                case InfractionAction.DeleteOnly:
                default:
                    break;
            }
        } catch (Exception ex) {
            logger.LogWarning(ex,
                "Failed to apply detection action {Action} to user {UserId} in channel {BroadcasterId}",
                action, twitchUserId, broadcasterId);
        }
    }

    private string DecryptToken(string encrypted) {
        return AesCrypto.DecryptString(encrypted, _encKey!, _encIv!);
    }

    private (string? AccessToken, string? RefreshToken) LoadPersistedTokens() {
        try {
            if (!File.Exists(TokenFilePath))
                return (null, null);

            var json = File.ReadAllText(TokenFilePath);
            var tokens = JsonSerializer.Deserialize<PersistedBotTokens>(json);
            if (tokens is null)
                return (null, null);

            var access = string.IsNullOrEmpty(tokens.AccessToken) ? null : AesCrypto.DecryptString(tokens.AccessToken, _encKey!, _encIv!);
            var refresh = string.IsNullOrEmpty(tokens.RefreshToken) ? null : AesCrypto.DecryptString(tokens.RefreshToken, _encKey!, _encIv!);
            return (access, refresh);
        } catch {
            return (null, null);
        }
    }

    private void PersistBotTokens(string accessToken, string refreshToken) {
        try {
            var encAccess = AesCrypto.EncryptString(accessToken, _encKey!, _encIv!);
            var encRefresh = AesCrypto.EncryptString(refreshToken, _encKey!, _encIv!);
            var json = JsonSerializer.Serialize(new PersistedBotTokens(encAccess, encRefresh), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(TokenFilePath, json);
        } catch {
            // Best-effort persistence — non-fatal if it fails
        }
    }

    private async Task<string> RefreshBotTokenAsync() {
        var content = new FormUrlEncodedContent(new Dictionary<string, string> {
            ["client_id"] = _twitchOptions.ClientId,
            ["client_secret"] = _twitchOptions.ClientSecret,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = _twitchOptions.BotRefreshToken!
        });

        var response = await _tokenHttp.PostAsync("https://id.twitch.tv/oauth2/token", content);

        if (!response.IsSuccessStatusCode) {
            var errorBody = await response.Content.ReadAsStringAsync();
            logger.LogError("Bot token refresh failed: {StatusCode} {ErrorBody}", response.StatusCode, errorBody);

            var fallback = _twitchOptions.BotAccessToken;
            if (!string.IsNullOrWhiteSpace(fallback)) {
                _botAccessToken = fallback.StartsWith("oauth:")
                    ? fallback["oauth:".Length..]
                    : fallback;
                logger.LogWarning("Falling back to static bot access token — it may be expired");
                return _botAccessToken;
            }

            throw new InvalidOperationException($"Bot token refresh failed ({response.StatusCode}) and no fallback access token is configured");
        }

        var token = await response.Content.ReadFromJsonAsync<TwitchTokenResponse>();
        if (token is null)
            throw new InvalidOperationException("Failed to deserialize token refresh response");

        _botAccessToken = token.AccessToken;
        _twitchOptions.BotRefreshToken = token.RefreshToken;

        if (_botApi is not null)
            _botApi.Settings.AccessToken = _botAccessToken;

        PersistBotTokens(token.AccessToken, token.RefreshToken);

        logger.LogInformation("Bot token refreshed and persisted successfully (expires in {ExpiresIn}s)", token.ExpiresIn);
        return token.AccessToken;
    }

    private async Task RefreshBotTokenPeriodicAsync() {
        try {
            await RefreshBotTokenAsync();
        } catch (Exception ex) {
            logger.LogError(ex, "Failed to refresh bot token periodically");
        }
    }

    private async Task RefreshChannelTokensPeriodicAsync() {
        try {
            await RefreshChannelTokensAsync();
        } catch (Exception ex) {
            logger.LogError(ex, "Failed to refresh channel tokens periodically");
        }
    }

    private async Task RefreshChannelTokensAsync() {
        var now = DateTime.UtcNow;
        var channels = await dbContext.TwitchChannelInstances
            .Where(c => c.IsActive && c.BotRefreshToken != null)
            .ToListAsync();

        int refreshed = 0;
        foreach (var ch in channels) {
            if (ch.BotTokenExpiresAtUtc is not null && ch.BotTokenExpiresAtUtc.Value > now.AddHours(1))
                continue;

            try {
                var refreshToken = AesCrypto.DecryptString(ch.BotRefreshToken!, _encKey!, _encIv!);
                var content = new FormUrlEncodedContent(new Dictionary<string, string> {
                    ["client_id"] = _twitchOptions.ClientId,
                    ["client_secret"] = _twitchOptions.ClientSecret,
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = refreshToken
                });

                var response = await _tokenHttp.PostAsync("https://id.twitch.tv/oauth2/token", content);
                if (!response.IsSuccessStatusCode) {
                    logger.LogWarning("Failed to refresh token for channel {Channel} ({UserId}): {StatusCode}", ch.Username, ch.TwitchUserId, response.StatusCode);
                    continue;
                }

                var token = await response.Content.ReadFromJsonAsync<TwitchTokenResponse>();
                if (token is null)
                    continue;

                ch.BotAccessToken = AesCrypto.EncryptString(token.AccessToken, _encKey!, _encIv!);
                ch.BotRefreshToken = AesCrypto.EncryptString(token.RefreshToken, _encKey!, _encIv!);
                ch.BotTokenExpiresAtUtc = now.AddSeconds(token.ExpiresIn);
                ch.UpdatedAtUtc = now;

                if (_channels.TryGetValue(ch.TwitchUserId, out var record)) {
                    _channels[ch.TwitchUserId] = record with { AccessToken = token.AccessToken };
                }

                refreshed++;
            } catch (Exception ex) {
                logger.LogError(ex, "Error refreshing token for channel {Channel} ({UserId})", ch.Username, ch.TwitchUserId);
            }
        }

        if (refreshed > 0) {
            await dbContext.SaveChangesAsync();
            logger.LogInformation("Refreshed tokens for {Count} channel(s)", refreshed);
        }
    }

    public async ValueTask DisposeAsync() {
        _tokenRefreshTimer?.Dispose();
        _channelTokenRefreshTimer?.Dispose();

        if (_client is not null && _client.IsConnected) {
            try {
                await _client.DisconnectAsync();
            } catch {
                // Ignore errors during shutdown
            }
        }

        _client = null;
        _tokenHttp.Dispose();
    }

}
