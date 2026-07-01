namespace Blackwall.Core.DTOs;

public sealed record ThreatLevelTestResponse(
    ulong UserId,
    int Score,
    string ThreatLevel,
    IReadOnlyList<string> Factors
);
