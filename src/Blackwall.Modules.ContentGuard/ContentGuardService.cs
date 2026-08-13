using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Blackwall.Core.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
// ReSharper disable UnusedVariable

namespace Blackwall.Modules.ContentGuard;

public sealed partial class ContentGuardService(
    IConnectionMultiplexer redis,
    IServiceScopeFactory scopeFactory
) {
    private readonly IDatabase _db = redis.GetDatabase();

    private static readonly Regex WordBoundaryPattern = WordBoundaryRegex();

    /// <summary>
    /// Evaluates a message against the guild's Content Guard configuration and banned word list.
    /// Returns the set of Content Guard violations found (e.g. "banned_word", "zalgo", "copypasta").
    /// </summary>
    public async Task<List<string>> EvaluateAsync(
        string content,
        long discordGuildId,
        long discordUserId,
        SpamConfigurationDto config,
        CancellationToken cancellationToken = default
    ) {
        var violations = new List<string>(4);

        if (!config.IsContentGuardEnabled)
            return violations;

        var scrubbed = config.ContentGuardInvisibleCharScrubbing
            ? ScrubInvisibleCharacters(content)
            : content;

        if (await ContainsBannedWordAsync(discordGuildId, scrubbed, config, cancellationToken))
            violations.Add("banned_word");

        if (config.ContentGuardZalgoBlocking && IsZalgo(content, config.ContentGuardZalgoMaxCombining))
            violations.Add("zalgo");

        if (config.ContentGuardCopypastaHashing && await IsCopypastaAsync(
                discordGuildId, discordUserId, content,
                config.ContentGuardCopypastaMinLength,
                config.ContentGuardCopypastaThreshold,
                config.ContentGuardCopypastaWindowSeconds))
            violations.Add("copypasta");

        return violations;
    }

    /// <summary>
    /// Evaluates a message against the Twitch channel's Content Guard configuration and banned word list.
    /// Returns the set of Content Guard violations found (e.g. "banned_word").
    /// Only banned words and fuzzy matching are evaluated for Twitch.
    /// </summary>
    public async Task<List<string>> EvaluateTwitchAsync(
        string content,
        long twitchUserId,
        bool fuzzyMatching,
        int fuzzyThreshold,
        CancellationToken cancellationToken = default
    ) {
        var violations = new List<string>(1);

        if (await ContainsTwitchBannedWordAsync(twitchUserId, content, fuzzyMatching, fuzzyThreshold, cancellationToken))
            violations.Add("banned_word");

        return violations;
    }

    /// <summary>
    /// Checks if the content contains any banned word configured for the given Twitch channel
    /// using exact match or fuzzy (Levenshtein distance) matching when enabled.
    /// Regex entries are matched with Regex.IsMatch (case-insensitive, 500ms timeout).
    /// </summary>
    private async Task<bool> ContainsTwitchBannedWordAsync(
        long twitchUserId,
        string content,
        bool fuzzyMatching,
        int fuzzyThreshold,
        CancellationToken cancellationToken
    ) {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.BlackwallDbContext>();

        var entries = await dbContext.TwitchChannelBannedWords
            .Where(w => w.TwitchChannelConfiguration.TwitchChannelInstance.TwitchUserId == twitchUserId)
            .Select(w => new { w.Word, w.IsRegex })
            .ToListAsync(cancellationToken);

        if (entries.Count == 0)
            return false;

        var regexEntries = entries.Where(e => e.IsRegex).ToList();
        if (regexEntries.Count > 0) {
            foreach (var entry in regexEntries) {
                try {
                    if (Regex.IsMatch(content, entry.Word, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(500)))
                        return true;
                } catch (ArgumentException) {
                    // Skip invalid regex patterns at evaluation time
                }
            }
        }

        var plainWords = entries.Where(e => !e.IsRegex).Select(e => e.Word).ToList();
        if (plainWords.Count == 0)
            return false;

        var normalisedContent = NormaliseLeetspeak(content);
        var tokens = WordBoundaryPattern.Split(normalisedContent)
            .Where(t => t.Length > 0)
            .ToList();

        foreach (var word in plainWords) {
            var normalisedWord = NormaliseLeetspeak(word);

            foreach (var token in tokens) {
                if (token.Equals(normalisedWord, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (fuzzyMatching
                    && token.Length >= 3
                    && normalisedWord.Length >= 3
                    && Math.Abs(token.Length - normalisedWord.Length) <= fuzzyThreshold
                    && LevenshteinDistance(token, normalisedWord) <= fuzzyThreshold)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if the scrubbed content contains any banned word using exact match or fuzzy
    /// (Levenshtein distance) matching when enabled. Words are compared on a per-token basis
    /// after normalising leetspeak substitutions.
    /// </summary>
    private async Task<bool> ContainsBannedWordAsync(
        long discordGuildId,
        string content,
        SpamConfigurationDto config,
        CancellationToken cancellationToken
    ) {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.BlackwallDbContext>();

        var entries = await dbContext.GuildBannedWords
            .Where(w => w.SpamConfiguration.GuildInstance.DiscordGuildId == discordGuildId)
            .Select(w => new { w.Word, w.IsRegex })
            .ToListAsync(cancellationToken);

        if (entries.Count == 0)
            return false;

        var regexEntries = entries.Where(e => e.IsRegex).ToList();
        if (regexEntries.Count > 0) {
            var scrubbed = config.ContentGuardInvisibleCharScrubbing
                ? ScrubInvisibleCharacters(content)
                : content;

            foreach (var entry in regexEntries) {
                try {
                    if (Regex.IsMatch(scrubbed, entry.Word, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(500)))
                        return true;
                } catch (ArgumentException) {
                    // Skip invalid regex patterns at evaluation time
                }
            }
        }

        var plainWords = entries.Where(e => !e.IsRegex).Select(e => e.Word).ToList();
        if (plainWords.Count == 0)
            return false;

        var normalisedContent = NormaliseLeetspeak(content);
        var tokens = WordBoundaryPattern.Split(normalisedContent)
            .Where(t => t.Length > 0)
            .ToList();

        foreach (var word in plainWords) {
            var normalisedWord = NormaliseLeetspeak(word);

            foreach (var token in tokens) {
                if (token.Equals(normalisedWord, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (config.ContentGuardFuzzyMatching
                    && token.Length >= 3
                    && normalisedWord.Length >= 3
                    && Math.Abs(token.Length - normalisedWord.Length) <= config.ContentGuardFuzzyThreshold
                    && LevenshteinDistance(token, normalisedWord) <= config.ContentGuardFuzzyThreshold)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Strips zero-width and other non-standard Unicode characters from the content.
    /// Removes: zero-width space (U+200B), zero-width non-joiner (U+200C), zero-width joiner
    /// (U+200D), word joiner (U+2060), zero-width no-break space / BOM (U+FEFF), and
    /// left/right-to-right marks (U+200E, U+200F).
    /// </summary>
    private static string ScrubInvisibleCharacters(string content) {
        var sb = new StringBuilder(content.Length);
        foreach (var ch in content) {
            if (ch is '\u200B' or '\u200C' or '\u200D' or '\u2060' or '\uFEFF' or '\u200E' or '\u200F')
                continue;
            sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Replaces common leetspeak substitutions to normalize text before comparison.
    /// </summary>
    private static string NormaliseLeetspeak(string input) {
        var sb = new StringBuilder(input.Length);
        foreach (var ch in input) {
            sb.Append(ch switch {
                '0' => 'o',
                '1' => 'i',
                '3' => 'e',
                '4' => 'a',
                '5' => 's',
                '7' => 't',
                '@' => 'a',
                '$' => 's',
                '!' => 'i',
                '+' => 't',
                _ => ch
            });
        }
        return sb.ToString();
    }

    /// <summary>
    /// Returns true if the content contains excessive Unicode combining characters
    /// (Zalgo text), i.e. any base character followed by more than <paramref name="maxCombining"/>
    /// consecutive combining marks.
    /// </summary>
    private static bool IsZalgo(string content, int maxCombining) {
        var consecutive = 0;
        foreach (var ch in content) {
            var category = char.GetUnicodeCategory(ch);
            if (category is System.Globalization.UnicodeCategory.NonSpacingMark
                or System.Globalization.UnicodeCategory.SpacingCombiningMark
                or System.Globalization.UnicodeCategory.EnclosingMark) {
                consecutive++;
                if (consecutive > maxCombining)
                    return true;
            } else {
                consecutive = 0;
            }
        }
        return false;
    }

    /// <summary>
    /// Tracks a hash of the message content across all users in the guild. Returns true if the
    /// same large text block has been posted by at least <paramref name="threshold"/> distinct
    /// users within <paramref name="windowSeconds"/>.
    /// </summary>
    private async Task<bool> IsCopypastaAsync(
        long discordGuildId,
        long discordUserId,
        string content,
        int minLength,
        int threshold,
        int windowSeconds
    ) {
        if (content.Length < minLength)
            return false;

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content.Trim())));
        var key = $"contentguard:copypasta:{discordGuildId}:{hash}";
        var userKey = $"contentguard:copypasta:{discordGuildId}:{hash}:users";

        // Add the user to the set of users who posted this hash
        await _db.SetAddAsync(userKey, discordUserId.ToString());
        await _db.KeyExpireAsync(userKey, TimeSpan.FromSeconds(windowSeconds));

        var distinctUsers = await _db.SetLengthAsync(userKey);
        return distinctUsers >= threshold;
    }

    /// <summary>
    /// Computes the Levenshtein edit distance between two strings.
    /// </summary>
    private static int LevenshteinDistance(string a, string b) {
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase))
            return 0;

        var n = a.Length;
        var m = b.Length;

        if (n == 0) return m;
        if (m == 0) return n;

        var prev = new int[m + 1];
        var curr = new int[m + 1];

        for (var j = 0; j <= m; j++)
            prev[j] = j;

        for (var i = 1; i <= n; i++) {
            curr[0] = i;
            for (var j = 1; j <= m; j++) {
                var cost = char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }

        return prev[m];
    }

    [GeneratedRegex(@"[^a-zA-Z0-9]+", RegexOptions.Compiled)]
    private static partial Regex WordBoundaryRegex();
}
