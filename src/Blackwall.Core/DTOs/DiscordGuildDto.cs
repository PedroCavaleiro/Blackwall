using System.Text.Json.Serialization;

namespace Blackwall.Core.DTOs;

public sealed record DiscordGuildDto(
    string Id,
    string Name,
    string? Icon,
    bool Owner,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    ulong Permissions = 0,
    string PermissionsNew = "0"
);