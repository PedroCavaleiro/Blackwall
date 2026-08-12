// ReSharper disable PropertyCanBeMadeInitOnly.Global
// ReSharper disable NullableWarningSuppressionIsUsed
namespace Blackwall.Core.Entities;

public sealed class TwitchChannelBan : EntityBase {
    public long TwitchChannelInstanceId { get; set; }
    public TwitchChannelInstance TwitchChannelInstance { get; set; } = null!;

    public long TwitchUserId { get; set; }
    public string? Username { get; set; }
    public string? Reason { get; set; }
    public DateTime? BannedAtUtc { get; set; }
}
