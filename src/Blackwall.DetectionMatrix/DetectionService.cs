using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Discord;
using StackExchange.Redis;
// ReSharper disable NullableWarningSuppressionIsUsed

namespace Blackwall.DetectionMatrix;

public sealed partial class DetectionService(string keyPrefix, IConnectionMultiplexer redis) {
    private readonly IDatabase _db = redis.GetDatabase();

    public async Task<bool> IsRateLimitedAsync(
        string scopeId,
        string userId,
        string messageId,
        int maxMessages,
        int windowSeconds
    ) {
        var key = $"{keyPrefix}ratelimit:{scopeId}:{userId}";
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var windowStart = now - windowSeconds * 1000L;

        await _db.SortedSetRemoveRangeByScoreAsync(key, 0, windowStart);
        await _db.SortedSetAddAsync(key, messageId, now);
        await _db.KeyExpireAsync(key, TimeSpan.FromSeconds(windowSeconds + 1));

        var count = await _db.SortedSetLengthAsync(key);

        return count > maxMessages;
    }

    public async Task<DuplicateDetectionResult> IsDuplicateAsync(
        string scopeId,
        string userId,
        string messageId,
        string content,
        int threshold,
        int windowSeconds,
        string? channelKey = null,
        bool crossChannel = false
    ) {
        if (string.IsNullOrWhiteSpace(content))
            return new DuplicateDetectionResult(false, []);

        var hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(content.Trim())));
        var key = channelKey is not null && !crossChannel
            ? $"{keyPrefix}dupes:{scopeId}:{userId}:{channelKey}:{hash}"
            : $"{keyPrefix}dupes:{scopeId}:{userId}:{hash}";
        var handledKey = $"{key}:handled";

        if (await _db.KeyExistsAsync(handledKey))
            return new DuplicateDetectionResult(true, []);

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var windowStart = now - windowSeconds * 1000L;

        await _db.SortedSetRemoveRangeByScoreAsync(key, 0, windowStart);

        var entry = channelKey is not null
            ? $"{channelKey}:{messageId}"
            : messageId;
        await _db.SortedSetAddAsync(key, entry, now);
        await _db.KeyExpireAsync(key, TimeSpan.FromSeconds(windowSeconds + 1));

        var count = await _db.SortedSetLengthAsync(key);

        if (count < threshold)
            return new DuplicateDetectionResult(false, []);

        var entries = await _db.SortedSetRangeByScoreAsync(key, windowStart, now);
        var messagesToDelete = new List<(string ChannelKey, string MessageId)>(entries.Length);

        foreach (var value in entries) {
            var str = (string)value!;
            if (channelKey is not null) {
                var parts = str.Split(':', 2);
                if (parts.Length == 2)
                    messagesToDelete.Add((parts[0], parts[1]));
            } else {
                messagesToDelete.Add((string.Empty, str));
            }
        }

        await _db.KeyDeleteAsync(key);
        await _db.StringSetAsync(handledKey, "1", TimeSpan.FromSeconds(windowSeconds));

        return new DuplicateDetectionResult(true, messagesToDelete);
    }

    public static bool ExceedsMentionLimit(IMessage message, int limit) {
        var count = message.MentionedUserIds.Count + message.MentionedRoleIds.Count;

        if (message.MentionedEveryone)
            count++;

        return count > limit;
    }

    public static bool ExceedsMentionLimit(string content, int limit) {
        if (limit <= 0)
            return false;

        var count = MentionPattern.Matches(content).Count;
        return count > limit;
    }

    public static string ExtractFullContent(IMessage message) {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(message.Content))
            parts.Add(message.Content.Trim());

        foreach (var attachment in message.Attachments) {
            if (!string.IsNullOrWhiteSpace(attachment.Filename))
                parts.Add(attachment.Filename.Trim());
            parts.Add($"att:{attachment.Filename}:{attachment.Size}:{attachment.Width}:{attachment.Height}");
        }

        foreach (var embed in message.Embeds) {
            if (!string.IsNullOrWhiteSpace(embed.Title))
                parts.Add(embed.Title.Trim());
            if (!string.IsNullOrWhiteSpace(embed.Description))
                parts.Add(embed.Description.Trim());
            if (!string.IsNullOrWhiteSpace(embed.Url))
                parts.Add(embed.Url.Trim());
            if (embed.Author is { } author && !string.IsNullOrWhiteSpace(author.Name))
                parts.Add(author.Name.Trim());
            if (embed.Footer is { } footer && !string.IsNullOrWhiteSpace(footer.Text))
                parts.Add(footer.Text.Trim());
            if (embed.Image is { } image && !string.IsNullOrWhiteSpace(image.Url))
                parts.Add(image.Url.Trim());
            if (embed.Thumbnail is { } thumbnail && !string.IsNullOrWhiteSpace(thumbnail.Url))
                parts.Add(thumbnail.Url.Trim());
            foreach (var field in embed.Fields) {
                if (!string.IsNullOrWhiteSpace(field.Name))
                    parts.Add(field.Name.Trim());
                if (!string.IsNullOrWhiteSpace(field.Value))
                    parts.Add(field.Value.Trim());
            }
        }

        return string.Join('\n', parts);
    }

    [GeneratedRegex(@"@\w+", RegexOptions.Compiled)]
    private static partial Regex MentionPattern { get; }
}

public sealed record DuplicateDetectionResult(
    bool IsDuplicate,
    IReadOnlyList<(string ChannelKey, string MessageId)> MessagesToDelete
);
