using Blackwall.Core.Entities;

namespace Blackwall.Core.DTOs;

public sealed record UpdateGuildSettingsRequest(
    int MaxMessagesPerWindow,
    int RateLimitWindowSeconds,
    int DuplicateMessageThreshold,
    int DuplicateWindowSeconds,
    bool DuplicateCrossChannelEnabled,
    int MentionLimit,
    bool BlockInviteLinks,
    bool BlockSuspiciousLinks,
    bool IsEnabled,
    bool IsDryRun,
    InfractionAction Action,
    long? LogChannelId,
    int MessageDeleteDays,
    bool IsAntiRaidEnabled,
    int AntiRaidJoinThreshold,
    int AntiRaidWindowSeconds,
    int AntiRaidCooldownMinutes,
    bool IsLockedDown,
    bool AutoLockdownEnabled
);
