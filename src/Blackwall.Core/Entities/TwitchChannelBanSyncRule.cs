// ReSharper disable PropertyCanBeMadeInitOnly.Global
// ReSharper disable NullableWarningSuppressionIsUsed
namespace Blackwall.Core.Entities;

public sealed class TwitchChannelBanSyncRule : EntityBase {
    public long TargetTwitchChannelInstanceId { get; set; }
    public TwitchChannelInstance TargetTwitchChannelInstance { get; set; } = null!;

    public long SourceTwitchUserId { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime LastSyncedAtUtc { get; set; }
}
