using Blackwall.Core.Entities;

namespace Blackwall.Core.DTOs;

public sealed record NetWatchSnareChannelDto(
    long Id,
    long DiscordChannelId,
    string ChannelName,
    NetWatchSnareAction Action,
    int TimeoutMinutes,
    int MessageDeleteDays,
    long? AssignRoleId,
    bool IsEnabled
);

public sealed record CreateNetWatchSnareChannelRequest(
    long DiscordChannelId,
    string ChannelName,
    NetWatchSnareAction Action,
    int TimeoutMinutes,
    int MessageDeleteDays,
    long? AssignRoleId
);

public sealed record UpdateNetWatchSnareChannelRequest(
    NetWatchSnareAction Action,
    int TimeoutMinutes,
    int MessageDeleteDays,
    long? AssignRoleId,
    bool IsEnabled
);
