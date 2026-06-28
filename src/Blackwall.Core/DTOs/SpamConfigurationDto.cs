namespace Blackwall.Core.DTOs;

public sealed record SpamConfigurationDto(
    int MaxMessagesPerWindow,
    int RateLimitWindowSeconds,
    int DuplicateMessageThreshold,
    int MentionLimit,
    bool BlockInviteLinks,
    bool BlockSuspiciousLinks
);
