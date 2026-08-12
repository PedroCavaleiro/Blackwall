// ReSharper disable PropertyCanBeMadeInitOnly.Global
// ReSharper disable NullableWarningSuppressionIsUsed
namespace Blackwall.Core.Entities;

public class TwitchMessageAuditEvent : EntityBase {
    public long TwitchChannelInstanceId { get; set; }
    public TwitchChannelInstance TwitchChannelInstance { get; set; } = null!;

    public long TwitchUserId { get; set; }
    public string Username { get; set; } = "";

    public string Violations { get; set; } = "";
    public InfractionAction Action { get; set; }
    public bool IsDryRun { get; set; }

    public ICollection<TwitchMessageAuditRecord> Records { get; set; } = [];
}
