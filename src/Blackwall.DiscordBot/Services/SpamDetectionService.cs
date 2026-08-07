using System.Security.Cryptography;
using System.Text;
using Discord;
using StackExchange.Redis;
// ReSharper disable NullableWarningSuppressionIsUsed

namespace Blackwall.DiscordBot.Services;

public sealed partial class SpamDetectionService(IConnectionMultiplexer redis) {
    private readonly IDatabase _db = redis.GetDatabase();

    /// <summary>
    /// Tracks per-user per-guild message timestamps in a Redis sorted set. Returns true if the count
    /// exceeds <paramref name="maxMessages"/> within a true sliding window of
    /// <paramref name="windowSeconds"/> seconds.
    /// </summary>
    /// <param name="discordGuildId">The Discord ID of the guild where the message was sent.</param>
    /// <param name="discordUserId">The Discord ID of the user who sent the message.</param>
    /// <param name="messageId">The Discord ID of the message being checked.</param>
    /// <param name="maxMessages">The maximum number of messages allowed within the window.</param>
    /// <param name="windowSeconds">The sliding window in seconds; entries older than this are removed before counting.</param>
    /// <returns><see langword="true"/> if the user's message count exceeds <paramref name="maxMessages"/> within the window; otherwise <see langword="false"/>.</returns>
    /// <exception cref="RedisException">Thrown when a Redis operation fails.</exception>
    public async Task<bool> IsRateLimitedAsync(
        long discordGuildId,
        long discordUserId,
        ulong messageId,
        int maxMessages,
        int windowSeconds
    ) {
        var key = $"spam:ratelimit:{discordGuildId}:{discordUserId}";
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var windowStart = now - windowSeconds * 1000L;

        await _db.SortedSetRemoveRangeByScoreAsync(key, 0, windowStart);
        await _db.SortedSetAddAsync(key, messageId.ToString(), now);
        await _db.KeyExpireAsync(key, TimeSpan.FromSeconds(windowSeconds + 1));

        var count = await _db.SortedSetLengthAsync(key);

        return count > maxMessages;
    }

    /// <summary>
    /// Tracks message content hashes per user per guild in a Redis sorted set keyed by timestamp.
    /// Returns a result indicating whether the same hash has been seen at least
    /// <paramref name="threshold"/> times within a true sliding window of
    /// <paramref name="windowSeconds"/> seconds, along with all the message IDs that share
    /// that hash so they can be bulk-deleted.
    /// When <paramref name="crossChannelEnabled"/> is true, duplicates are counted across all channels;
    /// when false, only messages within the same <paramref name="channelId"/> are counted.
    /// </summary>
    /// <param name="discordGuildId">The Discord ID of the guild where the message was sent.</param>
    /// <param name="discordUserId">The Discord ID of the user who sent the message.</param>
    /// <param name="channelId">The Discord ID of the channel where the message was sent.</param>
    /// <param name="messageId">The Discord ID of the message being checked.</param>
    /// <param name="content">The text content of the message to hash for duplicate detection.</param>
    /// <param name="threshold">The number of repeated messages required to trigger a duplicate detection.</param>
    /// <param name="windowSeconds">The sliding window in seconds; entries older than this are removed before counting.</param>
    /// <param name="crossChannelEnabled">Whether duplicates should be counted across all channels or only within the same channel.</param>
    /// <returns>A <see cref="DuplicateDetectionResult"/> indicating whether a duplicate was detected and the messages to delete.</returns>
    /// <exception cref="RedisException">Thrown when a Redis operation fails.</exception>
    public async Task<DuplicateDetectionResult> IsDuplicateAsync(
        long discordGuildId,
        long discordUserId,
        ulong channelId,
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
        var handledKey = $"{key}:handled";

        if (await _db.KeyExistsAsync(handledKey))
            return new DuplicateDetectionResult(true, []);

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var windowStart = now - windowSeconds * 1000L;

        await _db.SortedSetRemoveRangeByScoreAsync(key, 0, windowStart);

        var entry = $"{channelId}:{messageId}";
        await _db.SortedSetAddAsync(key, entry, now);
        await _db.KeyExpireAsync(key, TimeSpan.FromSeconds(windowSeconds + 1));

        var count = await _db.SortedSetLengthAsync(key);

        if (count < threshold)
            return new DuplicateDetectionResult(false, []);

        var entries = await _db.SortedSetRangeByScoreAsync(key, windowStart, now);
        var messagesToDelete = new List<(ulong ChannelId, ulong MessageId)>(entries.Length);

        foreach (var value in entries) {
            var parts = ((string)value!).Split(':', 2);
            if (parts.Length == 2
                && ulong.TryParse(parts[0], out var chId)
                && ulong.TryParse(parts[1], out var msgId)) {
                messagesToDelete.Add((chId, msgId));
            }
        }

        await _db.KeyDeleteAsync(key);
        await _db.StringSetAsync(handledKey, "1", TimeSpan.FromSeconds(windowSeconds));

        return new DuplicateDetectionResult(true, messagesToDelete);
    }

    /// <summary>
    /// Extracts all textual content from a message, including its raw content and the text
    /// fields of every embed (title, description, fields, footer, author, url). Used for
    /// duplicate detection on messages that may have empty content but rich embeds.
    /// </summary>
    /// <param name="message">The message to extract text from.</param>
    /// <returns>A single string joining all textual content found in the message and its embeds.</returns>
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

    /// <summary>
    /// Returns true if the total number of user mentions, role mentions, or @everyone/@here
    /// in the message exceeds the configured limit.
    /// </summary>
    /// <param name="message">The message to check for mentions.</param>
    /// <param name="limit">The maximum allowed number of mentions before the limit is exceeded.</param>
    /// <returns><see langword="true"/> if the total mention count exceeds <paramref name="limit"/>; otherwise <see langword="false"/>.</returns>
    public static bool ExceedsMentionLimit(IMessage message, int limit) {
        var count = message.MentionedUserIds.Count + message.MentionedRoleIds.Count;

        if (message.MentionedEveryone)
            count++;

        return count > limit;
    }

}

public sealed record DuplicateDetectionResult(
    bool IsDuplicate,
    IReadOnlyList<(ulong ChannelId, ulong MessageId)> MessagesToDelete
);
