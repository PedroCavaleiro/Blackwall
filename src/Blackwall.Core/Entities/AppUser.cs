namespace Blackwall.Core.Entities;

public sealed class AppUser: EntityBase {

    public long DiscordUserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? DisplayName { get; set; }

    public string? DiscordAccessToken { get; set; }
    public string? DiscordRefreshToken { get; set; }
    public DateTime? DiscordTokenExpiresAtUtc { get; set; }

    public long? TwitchUserId { get; set; }
    public string? TwitchUsername { get; set; }
    public string? TwitchDisplayName { get; set; }
    public string? TwitchProfileImageUrl { get; set; }
    public string? TwitchAccessToken { get; set; }
    public string? TwitchRefreshToken { get; set; }
    public DateTime? TwitchTokenExpiresAtUtc { get; set; }

    public string? ActiveDisplayNameProvider { get; set; }
    public bool LinkAccountsWarningDismissed { get; set; }

    public ICollection<GuildInstance> OwnedGuilds { get; set; } = [];
    public ICollection<GuildManager> ManagedGuilds { get; set; } = [];
    public ICollection<TwitchChannelInstance> OwnedTwitchChannels { get; set; } = [];
    public ICollection<TwitchChannelManager> ManagedTwitchChannels { get; set; } = [];

}