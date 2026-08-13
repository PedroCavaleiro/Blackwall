using System.Text.RegularExpressions;
using Blackwall.Core.Entities;
using Discord;

namespace Blackwall.Modules.DiscordAccountScoring;

public sealed partial class AccountScoringService {
    private const int MinAccountAgeDays = 1;
    private const int RecentAccountAgeDays = 7;
    private const int SomewhatRecentAccountAgeDays = 30;

    private static readonly Regex NumericOnlyPattern = NumericOnlyPatternRegex();
    private static readonly Regex ConsecutiveConsonantsPattern = ConsecutiveConsonantsPatternRegex();
    private static readonly HashSet<char> Vowels = ['a', 'e', 'i', 'o', 'u'];

    /// <summary>
    /// Scores a guild user based on account metadata: account age, avatar presence,
    /// and username patterns. Returns a <see cref="AccountScoreResult"/> containing
    /// the numeric score, threat level, and breakdown of contributing factors.
    /// </summary>
    public static AccountScoreResult ScoreUser(IGuildUser user) {
        var factors = new List<string>(4);
        var score = 0;

        var accountAgeDays = (DateTimeOffset.UtcNow - user.CreatedAt).TotalDays;

        switch (accountAgeDays) {
            case < MinAccountAgeDays:
                score += 3;
                factors.Add($"Account created < {MinAccountAgeDays} day ago");
                break;
            case < RecentAccountAgeDays:
                score += 2;
                factors.Add($"Account created < {RecentAccountAgeDays} days ago");
                break;
            case < SomewhatRecentAccountAgeDays:
                score += 1;
                factors.Add($"Account created < {SomewhatRecentAccountAgeDays} days ago");
                break;
        }

        if (user.GetAvatarUrl(size: 16) is null) {
            score += 2;
            factors.Add("No profile picture (default avatar)");
        }

        var username = user.Username;
        if (NumericOnlyPattern.IsMatch(username)) {
            score += 2;
            factors.Add("Username is purely numeric");
        } else if (IsGibberish(username)) {
            score += 2;
            factors.Add("Username appears to be gibberish (very low vowel ratio)");
        } else if (ConsecutiveConsonantsPattern.IsMatch(username)) {
            score += 1;
            factors.Add("Username has excessive consecutive consonants");
        }

        var threatLevel = score switch {
            >= 4 => ThreatLevel.High,
            >= 2 => ThreatLevel.Medium,
            _ => ThreatLevel.Low
        };

        return new AccountScoreResult(score, threatLevel, factors);
    }

    /// <summary>
    /// Returns true if the username is 6+ characters, alphanumeric, and has a vowel
    /// ratio below 15% — a strong indicator of randomly generated gibberish.
    /// Normal usernames like "david1" or "pedro99" have sufficient vowels and pass.
    /// </summary>
    private static bool IsGibberish(string username) {
        if (username.Length < 6)
            return false;

        var letters = username.Where(char.IsLetterOrDigit).ToArray();
        if (letters.Length < 6)
            return false;

        var vowelCount = letters.Count(c => Vowels.Contains(char.ToLowerInvariant(c)));
        return (double)vowelCount / letters.Length < 0.15;
    }

    [GeneratedRegex(@"^\d+$", RegexOptions.Compiled, "en-US")]
    private static partial Regex NumericOnlyPatternRegex();

    [GeneratedRegex(@"[^aeiou0-9]{7,}", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex ConsecutiveConsonantsPatternRegex();
}

public sealed record AccountScoreResult(
    int Score,
    ThreatLevel ThreatLevel,
    IReadOnlyList<string> Factors
);
