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
    public string LoginScopes { get; set; } = "openid user:read:email user:read:moderated_channels";

    [ConfigurationKeyName("BOT_SCOPES")]
    public string BotScopes { get; set; } = "chat:read chat:edit channel:moderate channel:read:subscriptions moderation:read openid channel:manage:redemptions channel:read:editors moderator:manage:automod moderator:read:automod_settings moderator:manage:automod_settings moderator:manage:banned_users moderator:read:blocked_terms moderator:manage:blocked_terms moderator:read:chat_settings moderator:manage:chat_settings moderator:manage:chat_messages channel:manage:moderators channel:read:vips channel:manage:vips moderator:read:chatters moderator:read:shield_mode moderator:manage:shield_mode moderator:read:shoutouts moderator:manage:shoutouts moderator:read:followers channel:bot user:read:chat moderator:read:unban_requests moderator:manage:unban_requests moderator:read:suspicious_users moderator:manage:warnings moderator:read:banned_users moderator:read:chat_messages moderator:read:moderators moderator:manage:suspicious_users moderator:read:vips moderator:read:warnings";

    [ConfigurationKeyName("BOT_REDIRECT_URI")]
    public string? BotRedirectUri { get; set; }

    [ConfigurationKeyName("BOT_USERNAME")]
    public string? BotUsername { get; set; }

    [ConfigurationKeyName("BOT_ACCESS_TOKEN")]
    public string? BotAccessToken { get; set; }

    [ConfigurationKeyName("BOT_REFRESH_TOKEN")]
    public string? BotRefreshToken { get; set; }
}
