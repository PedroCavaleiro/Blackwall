using Blackwall.Core.Entities;

namespace Blackwall.Core.DTOs;

public sealed record SentinelChannelDto(
    long Id,
    long DiscordChannelId,
    string ChannelName,
    SentinelAction Action,
    int TimeoutMinutes,
    int MessageDeleteDays,
    long? AssignRoleId,
    bool IsEnabled
);

public sealed record CreateSentinelChannelRequest(
    long DiscordChannelId,
    string ChannelName,
    SentinelAction Action,
    int TimeoutMinutes,
    int MessageDeleteDays,
    long? AssignRoleId
);

public sealed record UpdateSentinelChannelRequest(
    SentinelAction Action,
    int TimeoutMinutes,
    int MessageDeleteDays,
    long? AssignRoleId,
    bool IsEnabled
);
