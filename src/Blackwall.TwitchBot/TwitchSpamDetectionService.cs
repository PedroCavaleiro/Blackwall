using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using StackExchange.Redis;
// ReSharper disable NullableWarningSuppressionIsUsed

namespace Blackwall.TwitchBot;

public sealed partial class TwitchSpamDetectionService(IConnectionMultiplexer redis) {
    private readonly IDatabase _db = redis.GetDatabase();

    private static readonly Regex MentionPattern = MentionRegex();

    public async Task<bool> IsRateLimitedAsync(
        long broadcasterId,
        long twitchUserId,
        string messageId,
        int maxMessages,
        int windowSeconds
    ) {
        var key = $"twitch:ratelimit:{broadcasterId}:{twitchUserId}";
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var windowStart = now - windowSeconds * 1000L;

        await _db.SortedSetRemoveRangeByScoreAsync(key, 0, windowStart);
        await _db.SortedSetAddAsync(key, messageId, now);
        await _db.KeyExpireAsync(key, TimeSpan.FromSeconds(windowSeconds + 1));

        var count = await _db.SortedSetLengthAsync(key);

        return count > maxMessages;
    }

    public async Task<bool> IsDuplicateAsync(
        long broadcasterId,
        long twitchUserId,
        string messageId,
        string content,
        int threshold,
        int windowSeconds
    ) {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        var hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(content.Trim())));
        var key = $"twitch:dupes:{broadcasterId}:{twitchUserId}:{hash}";
        var handledKey = $"{key}:handled";

        if (await _db.KeyExistsAsync(handledKey))
            return true;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var windowStart = now - windowSeconds * 1000L;

        await _db.SortedSetRemoveRangeByScoreAsync(key, 0, windowStart);
        await _db.SortedSetAddAsync(key, messageId, now);
        await _db.KeyExpireAsync(key, TimeSpan.FromSeconds(windowSeconds + 1));

        var count = await _db.SortedSetLengthAsync(key);

        if (count < threshold)
            return false;

        await _db.KeyDeleteAsync(key);
        await _db.StringSetAsync(handledKey, "1", TimeSpan.FromSeconds(windowSeconds));

        return true;
    }

    public static bool ExceedsMentionLimit(string content, int limit) {
        if (limit <= 0)
            return false;

        var count = MentionPattern.Matches(content).Count;
        return count > limit;
    }

    [GeneratedRegex(@"@\w+", RegexOptions.Compiled)]
    private static partial Regex MentionRegex();
}
