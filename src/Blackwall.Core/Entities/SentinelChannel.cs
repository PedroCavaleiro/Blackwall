// ReSharper disable PropertyCanBeMadeInitOnly.Global
namespace Blackwall.Core.Entities;

public class SentinelChannel : EntityBase {
    public long SpamConfigurationId { get; set; }
    public SpamConfiguration SpamConfiguration { get; set; } = null!;

    public long DiscordChannelId { get; set; }
    public string ChannelName { get; set; } = null!;

    public SentinelAction Action { get; set; } = SentinelAction.SoftBan;
    public int TimeoutMinutes { get; set; } = 60;
    public int MessageDeleteDays { get; set; } = 1;
    public long? AssignRoleId { get; set; }

    public bool IsEnabled { get; set; } = true;
    public DateTime? UpdatedAtUtc { get; set; }
}
