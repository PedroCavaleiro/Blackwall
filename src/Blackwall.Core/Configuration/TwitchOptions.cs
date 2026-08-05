// ReSharper disable NullableWarningSuppressionIsUsed
// ReSharper disable UnusedAutoPropertyAccessor.Global

using Microsoft.Extensions.Configuration;

namespace Blackwall.Core.Configuration;

public sealed record TwitchOptions {
    public const string SectionName = "TWITCH";

    [ConfigurationKeyName("CLIENT_ID")]
    public required string ClientId { get; set; }
    [ConfigurationKeyName("CLIENT_SECRET")]
    public required string ClientSecret { get; set; }
    [ConfigurationKeyName("REDIRECT_URI")]
    public required string RedirectUri { get; set; }

    [ConfigurationKeyName("LOGIN_SCOPES")]
    public string LoginScopes { get; set; } = "openid user:read:email";

    [ConfigurationKeyName("BOT_SCOPES")]
    public string BotScopes { get; set; } = "channel:moderate channel:read:redemption channel:manage:redemption moderator:manage:automod chat:read chat:edit";

    [ConfigurationKeyName("BOT_REDIRECT_URI")]
    public string? BotRedirectUri { get; set; }
}
