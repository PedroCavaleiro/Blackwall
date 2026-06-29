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
    /// Tracks message content hashes per user per guild. Returns true when the same hash
    /// is seen at least <paramref name="threshold"/> times within <paramref name="windowSeconds"/>.
    /// When <paramref name="crossChannelEnabled"/> is true, duplicates are counted across all channels;
    /// when false, only messages within the same <paramref name="channelId"/> are counted.
    /// </summary>
    public async Task<bool> IsDuplicateAsync(
        long discordGuildId,
        long discordUserId,
        long channelId,
        string content,
        int threshold,
        int windowSeconds,
        bool crossChannelEnabled
    ) {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        var hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(content.Trim())));
        var key = crossChannelEnabled
            ? $"spam:dupes:{discordGuildId}:{discordUserId}:{hash}"
            : $"spam:dupes:{discordGuildId}:{discordUserId}:{channelId}:{hash}";
        var count = await _db.StringIncrementAsync(key);

        if (count == 1)
            await _db.KeyExpireAsync(key, TimeSpan.FromSeconds(windowSeconds));

        return count >= threshold;
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
    public static bool ContainsInviteLink(string content) =>
        InviteLinkPattern.IsMatch(content);

    /// <summary>Returns true if the content contains any non-Discord URL.</summary>
    public static bool ContainsSuspiciousLink(string content) =>
        SuspiciousLinkPattern.IsMatch(content);
    
    [GeneratedRegex(@"discord(?:\.gg|\.com/invite)/[a-zA-Z0-9-]+", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex InviteLinkPatternRegex();
    [GeneratedRegex(@"https?://(?!(?:cdn\.discordapp\.com|media\.discordapp\.net|discord\.com|discord\.gg))[^\s]+", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex SuspiciousLinkPatternRegex();
}
