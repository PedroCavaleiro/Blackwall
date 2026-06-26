namespace Blackwall.Core.DTOs;

public sealed record DiscordGuildDto(
    string Id,
    string Name,
    string? Icon,
    bool Owner,
    ulong Permissions = 0,
    string PermissionsNew = "0"
);