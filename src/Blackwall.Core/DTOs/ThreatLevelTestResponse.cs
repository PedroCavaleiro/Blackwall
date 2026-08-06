// ReSharper disable NotAccessedPositionalProperty.Global
namespace Blackwall.Core.DTOs;

public sealed record ThreatLevelTestResponse(
    ulong UserId,
    int Score,
    string ThreatLevel,
    IReadOnlyList<string> Factors,
    IReadOnlyList<ThreatLevelTestNotifiedGuild> NotifiedGuilds
);

public sealed record ThreatLevelTestNotifiedGuild(
    ulong GuildId,
    string GuildName,
    ulong? LogChannelId,
    bool Sent
);
