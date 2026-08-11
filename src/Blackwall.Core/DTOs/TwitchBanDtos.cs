// ReSharper disable NotAccessedPositionalProperty.Global
namespace Blackwall.Core.DTOs;

public sealed record TwitchChannelBanResponse(
    long Id,
    long TwitchUserId,
    string? Username,
    string? Reason,
    DateTime? BannedAtUtc
);

public sealed record SharedBanListTwitchChannelResponse(
    long TwitchUserId,
    string Username,
    string DisplayName,
    string? ProfileImageUrl,
    int BanCount
);

public sealed record ImportTwitchBansRequest(
    long SourceTwitchUserId,
    IReadOnlyList<long>? TwitchUserIds
);

public sealed record ImportTwitchBansResultResponse(
    int Imported,
    int Skipped,
    int Failed,
    IReadOnlyList<string> Errors
);

public sealed record UpdateTwitchShareBanListRequest(
    bool ShareBanList
);

public sealed record TwitchBanSyncRuleResponse(
    long Id,
    long SourceTwitchUserId,
    string SourceChannelName,
    bool IsEnabled,
    DateTime? LastSyncedAtUtc
);

public sealed record AddTwitchBanSyncRuleRequest(
    long SourceTwitchUserId
);

public sealed record UpdateTwitchBanSyncRuleRequest(
    bool IsEnabled
);
