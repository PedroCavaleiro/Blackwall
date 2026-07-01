// ReSharper disable NullableWarningSuppressionIsUsed
// ReSharper disable UnusedAutoPropertyAccessor.Global

using Microsoft.Extensions.Configuration;

namespace Blackwall.Core.Configuration;

public sealed record DiscordOptions {
    public const string SectionName = "DISCORD";

    [ConfigurationKeyName("BOT_TOKEN")]
    public required string BotToken { get; set; }
    [ConfigurationKeyName("CLIENT_ID")]
    public required string ClientId { get; set; }
    [ConfigurationKeyName("CLIENT_SECRET")]
    public required string ClientSecret { get; set; }
    [ConfigurationKeyName("REDIRECT_URI")]
    public required string RedirectUri { get; set; }
    [ConfigurationKeyName("BOT_PERMISSIONS")]
    public required string BotPermissions { get; set; }

    [ConfigurationKeyName("BOT_SCOPES")]
    public string BotScopes { get; set; } = "bot applications.commands";
    [ConfigurationKeyName("LOGIN_SCOPES")]
    public string LoginScopes { get; set; } = "identify guilds";
}