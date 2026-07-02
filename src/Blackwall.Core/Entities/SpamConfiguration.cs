// ReSharper disable PropertyCanBeMadeInitOnly.Global
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
    public long? LogChannelId { get; set; }

    public bool IsAntiRaidEnabled { get; set; }
    public int AntiRaidJoinThreshold { get; set; } = 10;
    public int AntiRaidWindowSeconds { get; set; } = 30;
    public int AntiRaidCooldownMinutes { get; set; } = 30;

    public bool IsAccountScoringEnabled { get; set; }
    public bool AutoTimeoutMediumRiskOnJoin { get; set; }
    public bool AutoTimeoutHighRiskOnJoin { get; set; }
    public int AccountScoringTimeoutMinutes { get; set; } = 10;

    public bool IsLockedDown { get; set; }

    public InfractionAction RateLimitAction { get; set; } = InfractionAction.Timeout;
    public bool RateLimitAutoLockdown { get; set; }
    public int RateLimitTimeoutMinutes { get; set; } = 10;
    public int RateLimitMessageDeleteDays { get; set; }

    public InfractionAction DuplicateAction { get; set; } = InfractionAction.Timeout;
    public bool DuplicateAutoLockdown { get; set; }
    public int DuplicateTimeoutMinutes { get; set; } = 10;
    public int DuplicateMessageDeleteDays { get; set; }

    public InfractionAction MentionLimitAction { get; set; } = InfractionAction.Timeout;
    public bool MentionLimitAutoLockdown { get; set; }
    public int MentionLimitTimeoutMinutes { get; set; } = 10;
    public int MentionLimitMessageDeleteDays { get; set; }

    public InfractionAction InviteLinkAction { get; set; } = InfractionAction.Timeout;
    public bool InviteLinkAutoLockdown { get; set; }
    public int InviteLinkTimeoutMinutes { get; set; } = 10;
    public int InviteLinkMessageDeleteDays { get; set; }

    public InfractionAction SuspiciousLinkAction { get; set; } = InfractionAction.Timeout;
    public bool SuspiciousLinkAutoLockdown { get; set; }
    public int SuspiciousLinkTimeoutMinutes { get; set; } = 10;
    public int SuspiciousLinkMessageDeleteDays { get; set; }

    public bool IsContentGuardEnabled { get; set; }
    public bool ContentGuardFuzzyMatching { get; set; } = true;
    public bool ContentGuardInvisibleCharScrubbing { get; set; } = true;
    public bool ContentGuardZalgoBlocking { get; set; } = true;
    public bool ContentGuardCopypastaHashing { get; set; } = true;
    public int ContentGuardFuzzyThreshold { get; set; } = 2;
    public int ContentGuardZalgoMaxCombining { get; set; } = 3;
    public int ContentGuardCopypastaMinLength { get; set; } = 200;
    public int ContentGuardCopypastaThreshold { get; set; } = 3;
    public int ContentGuardCopypastaWindowSeconds { get; set; } = 60;
    public InfractionAction ContentGuardAction { get; set; } = InfractionAction.DeleteOnly;
    public bool ContentGuardAutoLockdown { get; set; }
    public int ContentGuardTimeoutMinutes { get; set; } = 10;
    public int ContentGuardMessageDeleteDays { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public ICollection<GuildBlacklist> Blacklists { get; set; } = [];
    public ICollection<GuildBlacklistDomain> BlacklistDomains { get; set; } = [];
    public ICollection<GuildBannedWord> BannedWords { get; set; } = [];
}