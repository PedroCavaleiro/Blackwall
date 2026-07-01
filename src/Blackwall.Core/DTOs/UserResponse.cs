namespace Blackwall.Core.DTOs;

public sealed record UserResponse(
    long Id,
    long DiscordUserId,
    string Username,
    string? DisplayName
);