// ReSharper disable NullableWarningSuppressionIsUsed
// ReSharper disable PropertyCanBeMadeInitOnly.Global
namespace Blackwall.Core.Entities;

public sealed class TwitchChannelInstance : EntityBase {
    public long TwitchUserId { get; set; }
    public string Username { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? ProfileImageUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public long? OwnerUserId { get; set; }
    public AppUser? OwnerUser { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public ICollection<TwitchChannelManager> Managers { get; set; } = [];
}
