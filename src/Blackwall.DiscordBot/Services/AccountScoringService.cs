using System.Text.RegularExpressions;
using Blackwall.Core.Entities;
using Discord;

namespace Blackwall.DiscordBot.Services;

public sealed partial class AccountScoringService {
    private const int MinAccountAgeDays = 1;
    private const int RecentAccountAgeDays = 7;
    private const int SomewhatRecentAccountAgeDays = 30;

    private static readonly Regex GibberishPattern = GibberishPatternRegex();
    private static readonly Regex NumericOnlyPattern = NumericOnlyPatternRegex();
    private static readonly Regex ConsecutiveConsonantsPattern = ConsecutiveConsonantsPatternRegex();

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
        } else if (GibberishPattern.IsMatch(username)) {
            score += 2;
            factors.Add("Username appears to be alphanumeric gibberish");
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

    [GeneratedRegex(@"^[a-z0-9]{6,}$", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex GibberishPatternRegex();

    [GeneratedRegex(@"^\d+$", RegexOptions.Compiled, "en-US")]
    private static partial Regex NumericOnlyPatternRegex();

    [GeneratedRegex(@"[^aeiou0-9]{5,}", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex ConsecutiveConsonantsPatternRegex();
}

public sealed record AccountScoreResult(
    int Score,
    ThreatLevel ThreatLevel,
    IReadOnlyList<string> Factors
);
