using Blackwall.Core.Entities;

namespace Blackwall.Core.DTOs;

public sealed record TwitchChannelSettingsResponse(
    long TwitchUserId,
    string Username,
    string DisplayName,
    string? ProfileImageUrl,
    bool IsActive,
    bool IsOwner,
    bool IsEnabled,
    bool IsDryRun,
    bool AutoAddManagers,
    string CommandTrigger,
    int MaxMessagesPerWindow,
    int RateLimitWindowSeconds,
    int DuplicateMessageThreshold,
    int DuplicateWindowSeconds,
    int MentionLimit,
    InfractionAction RateLimitAction,
    int RateLimitTimeoutMinutes,
    InfractionAction DuplicateAction,
    int DuplicateTimeoutMinutes,
    InfractionAction MentionLimitAction,
    int MentionLimitTimeoutMinutes
);

public sealed record UpdateTwitchChannelSettingsRequest(
    bool IsEnabled,
    bool IsDryRun,
    bool AutoAddManagers,
    string CommandTrigger,
    int MaxMessagesPerWindow,
    int RateLimitWindowSeconds,
    int DuplicateMessageThreshold,
    int DuplicateWindowSeconds,
    int MentionLimit,
    InfractionAction RateLimitAction,
    int RateLimitTimeoutMinutes,
    InfractionAction DuplicateAction,
    int DuplicateTimeoutMinutes,
    InfractionAction MentionLimitAction,
    int MentionLimitTimeoutMinutes
);

public sealed record TwitchAllowedBotResponse(
    long Id,
    string BotUsername
);

public sealed record AddTwitchAllowedBotRequest(
    string BotUsername
);

public sealed record TwitchChannelManagerResponse(
    long Id,
    long UserId,
    string Username,
    string? DisplayName,
    string? ProfileImageUrl,
    bool IsAdmin
);

public sealed record AddTwitchChannelManagerRequest(
    string Username
);
