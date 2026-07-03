using Blackwall.Core.Entities;

namespace Blackwall.Core.DTOs;

public sealed record NetWatchSnareChannelDto(
    long Id,
    long DiscordChannelId,
    string ChannelName,
    InfractionAction Action,
    int TimeoutMinutes,
    int MessageDeleteDays,
    bool IsEnabled
);

public sealed record CreateNetWatchSnareChannelRequest(
    long DiscordChannelId,
    string ChannelName,
    InfractionAction Action,
    int TimeoutMinutes,
    int MessageDeleteDays
);

public sealed record UpdateNetWatchSnareChannelRequest(
    InfractionAction Action,
    int TimeoutMinutes,
    int MessageDeleteDays,
    bool IsEnabled
);
