// ReSharper disable NullableWarningSuppressionIsUsed
namespace Blackwall.Core.Entities;

public class SpamConfiguration: EntityBase {

    public long GuildInstanceId { get; set; }
    public GuildInstance GuildInstance { get; set; } = null!;

    public int MaxMessagesPerWindow { get; set; }
    public int RateLimitWindowSeconds { get; set; }
    public int DuplicateMessageThreshold { get; set; }
    public int DuplicateWindowSeconds { get; set; } = 5;
    public bool DuplicateCrossChannelEnabled { get; set; } = true;
    public int MentionLimit { get; set; }
    public bool BlockInviteLinks { get; set; }
    public bool BlockSuspiciousLinks { get; set; }

    public bool IsEnabled { get; set; } = true;
    public bool IsDryRun { get; set; }
    public InfractionAction Action { get; set; } = InfractionAction.DeleteOnly;
    public long? LogChannelId { get; set; }
    public int MessageDeleteDays { get; set; }

    public bool IsAntiRaidEnabled { get; set; }
    public int AntiRaidJoinThreshold { get; set; } = 10;
    public int AntiRaidWindowSeconds { get; set; } = 30;
    public int AntiRaidCooldownMinutes { get; set; } = 30;

    public bool IsLockedDown { get; set; }
    public bool AutoLockdownEnabled { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }
}