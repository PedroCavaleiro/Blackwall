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
    public bool LinkWhitelistMode { get; set; }
    public bool SafeBrowsingEnabled { get; set; }
    public bool SafeBrowsingBlockUnsure { get; set; }

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

    public InfractionAction? RateLimitAction { get; set; }
    public bool? RateLimitAutoLockdown { get; set; }

    public InfractionAction? DuplicateAction { get; set; }
    public bool? DuplicateAutoLockdown { get; set; }

    public InfractionAction? MentionLimitAction { get; set; }
    public bool? MentionLimitAutoLockdown { get; set; }

    public InfractionAction? InviteLinkAction { get; set; }
    public bool? InviteLinkAutoLockdown { get; set; }

    public InfractionAction? SuspiciousLinkAction { get; set; }
    public bool? SuspiciousLinkAutoLockdown { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public ICollection<GuildBlacklist> Blacklists { get; set; } = [];
    public ICollection<GuildBlacklistDomain> BlacklistDomains { get; set; } = [];
}