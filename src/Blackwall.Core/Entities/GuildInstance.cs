// ReSharper disable NullableWarningSuppressionIsUsed
namespace Blackwall.Core.Entities;

public sealed class GuildInstance {
    public long Id { get; set; }
    public long DiscordGuildId { get; set; }
    public string Name { get; set; } = null!;
    public string? IconHash { get; set; }

    public long OwnerUserId { get; set; }
    public AppUser OwnerUser { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public SpamConfiguration SpamConfiguration { get; set; } = null!;
    public ICollection<GuildManager> Managers { get; set; } = [];

}