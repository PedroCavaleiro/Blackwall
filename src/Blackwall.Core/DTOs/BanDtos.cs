namespace Blackwall.Core.DTOs;

public sealed record GuildBanResponse(
    long Id,
    long DiscordUserId,
    string? Username,
    string? Reason,
    DateTime? BannedAtUtc
);

public sealed record SharedBanListGuildResponse(
    long DiscordGuildId,
    string Name,
    string? IconHash,
    int BanCount
);

public sealed record ImportBansRequest(
    long SourceDiscordGuildId,
    IReadOnlyList<long>? DiscordUserIds
);

public sealed record ImportBansResultResponse(
    int Imported,
    int Skipped,
    int Failed,
    IReadOnlyList<string> Errors
);

public sealed record UpdateShareBanListRequest(
    bool ShareBanList
);

public sealed record BanSyncRuleResponse(
    long Id,
    long SourceDiscordGuildId,
    string SourceGuildName,
    bool IsEnabled,
    DateTime? LastSyncedAtUtc
);

public sealed record AddBanSyncRuleRequest(
    long SourceDiscordGuildId
);

public sealed record UpdateBanSyncRuleRequest(
    bool IsEnabled
);
