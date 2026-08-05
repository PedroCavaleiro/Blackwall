namespace Blackwall.Core.DTOs;

public sealed record UserResponse(
    long Id,
    long? DiscordUserId,
    long? TwitchUserId,
    string Username,
    string? DisplayName
);