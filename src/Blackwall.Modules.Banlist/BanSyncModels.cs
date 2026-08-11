namespace Blackwall.Modules.Banlist;

public sealed record PlatformBanRecord(
    long UserId,
    string? Username,
    string? Reason,
    DateTime? BannedAtUtc
);

public sealed record BanImportResult(
    int Imported,
    int Skipped,
    int Failed,
    List<string> Errors
);
