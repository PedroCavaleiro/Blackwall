using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Discord;
using StackExchange.Redis;

namespace Blackwall.Bot.Services;

public sealed partial class SpamDetectionService(IConnectionMultiplexer redis) {
    private readonly IDatabase _db = redis.GetDatabase();

    private static readonly Regex InviteLinkPattern = InviteLinkPatternRegex();
    private static readonly Regex SuspiciousLinkPattern = SuspiciousLinkPatternRegex();
    private static readonly Regex UrlPattern = UrlPatternRegex();

    private static readonly HttpClient RedirectHttpClient = new(new HttpClientHandler {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 10
    }) {
        Timeout = TimeSpan.FromSeconds(5)
    };

    /// <summary>
    /// Increments a per-user per-guild message counter in Redis. Returns true if the count
    /// exceeds <paramref name="maxMessages"/> within the rolling window.
    /// </summary>
    public async Task<bool> IsRateLimitedAsync(
        long discordGuildId,
        long discordUserId,
        int maxMessages,
        int windowSeconds
    ) {
        var key = $"spam:ratelimit:{discordGuildId}:{discordUserId}";
        var count = await _db.StringIncrementAsync(key);

        if (count == 1)
            await _db.KeyExpireAsync(key, TimeSpan.FromSeconds(windowSeconds));

        return count > maxMessages;
    }

    /// <summary>
    /// Tracks message content hashes per user per guild. Returns a result indicating whether
    /// the same hash has been seen at least <paramref name="threshold"/> times within
    /// <paramref name="windowSeconds"/>, along with all the message IDs that share that hash
    /// so they can be bulk-deleted.
    /// When <paramref name="crossChannelEnabled"/> is true, duplicates are counted across all channels;
    /// when false, only messages within the same <paramref name="channelId"/> are counted.
    /// </summary>
    public async Task<DuplicateDetectionResult> IsDuplicateAsync(
        long discordGuildId,
        long discordUserId,
        long channelId,
        ulong messageId,
        string content,
        int threshold,
        int windowSeconds,
        bool crossChannelEnabled
    ) {
        if (string.IsNullOrWhiteSpace(content))
            return new DuplicateDetectionResult(false, []);

        var hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(content.Trim())));
        var key = crossChannelEnabled
            ? $"spam:dupes:{discordGuildId}:{discordUserId}:{hash}"
            : $"spam:dupes:{discordGuildId}:{discordUserId}:{channelId}:{hash}";

        var entry = $"{channelId}:{messageId}";
        var count = await _db.ListRightPushAsync(key, entry);

        if (count == 1)
            await _db.KeyExpireAsync(key, TimeSpan.FromSeconds(windowSeconds));

        if (count < threshold)
            return new DuplicateDetectionResult(false, []);

        var entries = await _db.ListRangeAsync(key);
        var messagesToDelete = new List<(ulong ChannelId, ulong MessageId)>(entries.Length);

        foreach (var value in entries) {
            var parts = ((string)value!).Split(':', 2);
            if (parts.Length == 2
                && ulong.TryParse(parts[0], out var chId)
                && ulong.TryParse(parts[1], out var msgId)) {
                messagesToDelete.Add((chId, msgId));
            }
        }

        return new DuplicateDetectionResult(true, messagesToDelete);
    }

    /// <summary>
    /// Returns true if the total number of user mentions, role mentions, or @everyone/@here
    /// in the message exceeds the configured limit.
    /// </summary>
    public static bool ExceedsMentionLimit(IMessage message, int limit) {
        var count = message.MentionedUserIds.Count + message.MentionedRoleIds.Count;

        if (message.MentionedEveryone)
            count++;

        return count > limit;
    }

    /// <summary>Returns true if the content contains a Discord invite link.</summary>
    private static bool ContainsInviteLink(string content) =>
        InviteLinkPattern.IsMatch(content);

    /// <summary>
    /// Checks if the content contains a Discord invite link, either directly or hidden behind
    /// a URL shortener / redirect. First checks the raw content with the invite regex. If no
    /// direct match is found, extracts all URLs and follows redirects to their final destination,
    /// checking each resolved URL against the invite pattern.
    /// </summary>
    public static async Task<bool> ContainsInviteLinkWithRedirectAsync(string content) {
        if (ContainsInviteLink(content))
            return true;

        var urls = UrlPattern.Matches(content);
        if (urls.Count == 0)
            return false;

        foreach (Match match in urls) {
            var url = match.Value;

            if (InviteLinkPattern.IsMatch(url))
                return true;

            var redirectUrl = url.Contains("://") ? url : "https://" + url;
            var resolved = await ResolveRedirectAsync(redirectUrl);
            if (resolved is not null && InviteLinkPattern.IsMatch(resolved))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Follows HTTP redirects for the given URL using a HEAD request and returns the final
    /// destination URI, or <c>null</c> if the request fails or times out.
    /// </summary>
    private static async Task<string?> ResolveRedirectAsync(string url) {
        try {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("Blackwall/1.0 (+https://blackwall.app)");

            using var response = await RedirectHttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead);

            return response.RequestMessage?.RequestUri?.ToString();
        } catch {
            return null;
        }
    }

    /// <summary>Returns true if the content contains any non-Discord URL.</summary>
    public static bool ContainsSuspiciousLink(string content) =>
        SuspiciousLinkPattern.IsMatch(content);

    /// <summary>
    /// Checks if the content contains any URL whose domain (or redirect destination domain)
    /// should be blocked for the guild. In blacklist mode, blocked if the domain is in the
    /// blacklist or custom domain set. In whitelist mode, blocked if the domain is NOT in the
    /// custom domain set. Follows redirects to check the final destination as well.
    /// </summary>
    public static async Task<bool> ContainsBlacklistedLinkAsync(
        string content,
        BlacklistService blacklistService,
        long discordGuildId
    ) {
        var urls = UrlPattern.Matches(content);
        if (urls.Count == 0)
            return false;

        foreach (Match match in urls) {
            var url = match.Value;

            if (await blacklistService.IsLinkBlockedAsync(discordGuildId, url))
                return true;

            var redirectUrl = url.Contains("://") ? url : "https://" + url;
            var resolved = await ResolveRedirectAsync(redirectUrl);
            if (resolved is not null && await blacklistService.IsLinkBlockedAsync(discordGuildId, resolved))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Checks all non-Discord URLs in the content against Google Safe Browsing.
    /// Returns the worst result found (Unsafe > Unsure > Safe).
    /// Only URLs that are not Discord domains are checked.
    /// </summary>
    public static async Task<SafeBrowsingResult> CheckSafeBrowsingAsync(
        string content,
        SafeBrowsingService safeBrowsingService
    ) {
        var urls = UrlPattern.Matches(content);
        if (urls.Count == 0)
            return SafeBrowsingResult.Safe;

        var worst = SafeBrowsingResult.Safe;

        foreach (Match match in urls) {
            var url = match.Value;

            if (IsDiscordUrl(url))
                continue;

            var result = await safeBrowsingService.CheckUrlAsync(url);
            if (result == SafeBrowsingResult.Unsafe)
                return SafeBrowsingResult.Unsafe;
            if (result == SafeBrowsingResult.Unsure && worst == SafeBrowsingResult.Safe)
                worst = SafeBrowsingResult.Unsure;

            var redirectUrl = url.Contains("://") ? url : "https://" + url;
            var resolved = await ResolveRedirectAsync(redirectUrl);
            if (resolved is not null && !IsDiscordUrl(resolved)) {
                var resolvedResult = await safeBrowsingService.CheckUrlAsync(resolved);
                if (resolvedResult == SafeBrowsingResult.Unsafe)
                    return SafeBrowsingResult.Unsafe;
                if (resolvedResult == SafeBrowsingResult.Unsure && worst == SafeBrowsingResult.Safe)
                    worst = SafeBrowsingResult.Unsure;
            }
        }

        return worst;
    }

    /// <summary>
    /// Returns true if the URL points to a Discord-owned domain.
    /// </summary>
    private static bool IsDiscordUrl(string url) {
        if (!url.Contains("://"))
            url = "https://" + url;

        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Host.EndsWith("discord.gg", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith("discord.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith("discordapp.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith("discordapp.net", StringComparison.OrdinalIgnoreCase));
    }
    
    [GeneratedRegex(@"discord(?:\.gg|\.com/invite)/[a-zA-Z0-9-]+", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex InviteLinkPatternRegex();
    [GeneratedRegex(@"https?://(?!(?:cdn\.discordapp\.com|media\.discordapp\.net|discord\.com|discord\.gg))[^\s]+", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex SuspiciousLinkPatternRegex();
    [GeneratedRegex(@"(?:https?://)?[a-zA-Z0-9](?:[a-zA-Z0-9\-]*[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9\-]*[a-zA-Z0-9])?)+(?:/[^\s]*)?", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex UrlPatternRegex();
}

public sealed record DuplicateDetectionResult(
    bool IsDuplicate,
    IReadOnlyList<(ulong ChannelId, ulong MessageId)> MessagesToDelete
);
