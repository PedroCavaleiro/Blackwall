// ReSharper disable NullableWarningSuppressionIsUsed
// ReSharper disable PropertyCanBeMadeInitOnly.Global
namespace Blackwall.Core.Entities;

public sealed class TwitchChannelInstance : EntityBase {
    public long TwitchUserId { get; set; }
    public string Username { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? ProfileImageUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public bool ShareBanList { get; set; }
    public long? OwnerUserId { get; set; }
    public AppUser? OwnerUser { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public string? BotAccessToken { get; set; }
    public string? BotRefreshToken { get; set; }
    public DateTime? BotTokenExpiresAtUtc { get; set; }

    public TwitchChannelConfiguration? Configuration { get; set; }

    public ICollection<TwitchChannelManager> Managers { get; set; } = [];
    public ICollection<TwitchChannelBan> Bans { get; set; } = [];
    public ICollection<TwitchChannelBanSyncRule> BanSyncRules { get; set; } = [];
}
