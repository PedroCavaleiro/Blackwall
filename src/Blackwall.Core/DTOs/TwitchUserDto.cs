using System.Text.Json.Serialization;

namespace Blackwall.Core.DTOs;

public sealed record TwitchUserDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("login")] string Login,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("email")] string? Email = null,
    [property: JsonPropertyName("profile_image_url")] string? ProfileImageUrl = null
);
