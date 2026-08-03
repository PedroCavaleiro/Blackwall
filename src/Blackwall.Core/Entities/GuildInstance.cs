// ReSharper disable NullableWarningSuppressionIsUsed
// ReSharper disable PropertyCanBeMadeInitOnly.Global
namespace Blackwall.Core.Entities;

public sealed class GuildInstance: EntityBase {
    public long DiscordGuildId { get; set; }
    public string Name { get; set; } = null!;
    public string? IconHash { get; set; }
    public bool IsActive { get; set; } = true;
    public bool ShareBanList { get; set; }
    public long? OwnerUserId { get; set; }
    public AppUser? OwnerUser { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public SpamConfiguration SpamConfiguration { get; set; } = null!;
    public AiSentinelConfiguration AiSentinelConfiguration { get; set; } = null!;
    public ICollection<GuildManager> Managers { get; set; } = [];
    public ICollection<GuildBan> Bans { get; set; } = [];
    public ICollection<GuildBanSyncRule> BanSyncRules { get; set; } = [];

}