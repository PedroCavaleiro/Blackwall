namespace Blackwall.Core.DTOs;

public record DiscordCallbackResponse(
    UserResponse User,
    IReadOnlyList<DiscordGuildDto> Guilds
);