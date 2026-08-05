namespace Blackwall.Core.DTOs;

public sealed record LinkedAccountsResponse(
    bool HasDiscord,
    bool HasTwitch,
    string? DiscordUsername,
    string? DiscordDisplayName,
    long? DiscordUserId,
    string? TwitchUsername,
    string? TwitchDisplayName,
    long? TwitchUserId,
    string? ActiveDisplayNameProvider,
    string DisplayName
);

public sealed record UpdateDisplayNameProviderRequest(string Provider);

public sealed record UnlinkAccountWarningResponse(
    bool HasOrphanGuilds,
    IReadOnlyList<OrphanGuildWarning> OrphanGuilds
);

public sealed record OrphanGuildWarning(
    long DiscordGuildId,
    string Name
);
