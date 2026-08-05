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
    string CommandTrigger
);

public sealed record UpdateTwitchChannelSettingsRequest(
    bool IsEnabled,
    bool IsDryRun,
    bool AutoAddManagers,
    string CommandTrigger
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
