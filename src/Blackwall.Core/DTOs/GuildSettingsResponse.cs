namespace Blackwall.Core.DTOs;

public sealed record GuildSettingsResponse(
    long DiscordGuildId,
    string Name,
    string? IconHash,
    bool IsActive,
    bool ShareBanList,
    SpamConfigurationDto SpamConfiguration
);
