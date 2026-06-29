// ReSharper disable NullableWarningSuppressionIsUsed
namespace Blackwall.Core.Entities;

public class SpamConfiguration: EntityBase {

    public long GuildInstanceId { get; set; }
    public GuildInstance GuildInstance { get; set; } = null!;

    public int MaxMessagesPerWindow { get; set; }
    public int RateLimitWindowSeconds { get; set; }
    public int DuplicateMessageThreshold { get; set; }
    public int MentionLimit { get; set; }
    public bool BlockInviteLinks { get; set; }
    public bool BlockSuspiciousLinks { get; set; }

    public bool IsEnabled { get; set; } = true;
    public bool IsDryRun { get; set; }
    public InfractionAction Action { get; set; } = InfractionAction.DeleteOnly;
    public long? LogChannelId { get; set; }
    public int MessageDeleteDays { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }
}