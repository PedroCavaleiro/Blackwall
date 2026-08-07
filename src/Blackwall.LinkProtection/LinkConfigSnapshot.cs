namespace Blackwall.LinkProtection;

public sealed record LinkConfigSnapshot(
    bool LinkWhitelistMode,
    IReadOnlyList<string> BlacklistUrls,
    IReadOnlyList<string> CustomRules
);
