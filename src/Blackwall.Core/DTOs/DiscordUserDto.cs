using System.Text.Json.Serialization;

namespace Blackwall.Core.DTOs;

public sealed record DiscordUserDto(
    string Id,
    string Username,
    string? GlobalName,
    string? Avatar = null
);