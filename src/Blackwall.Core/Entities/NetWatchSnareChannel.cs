// ReSharper disable PropertyCanBeMadeInitOnly.Global
// ReSharper disable NullableWarningSuppressionIsUsed
namespace Blackwall.Core.Entities;

public class NetWatchSnareChannel : EntityBase {
    public long SpamConfigurationId { get; set; }
    public SpamConfiguration SpamConfiguration { get; set; } = null!;

    public long DiscordChannelId { get; set; }
    public string ChannelName { get; set; } = null!;

    public InfractionAction Action { get; set; } = InfractionAction.Timeout;
    public int TimeoutMinutes { get; set; } = 60;
    public int MessageDeleteDays { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;
    public DateTime? UpdatedAtUtc { get; set; }
}
