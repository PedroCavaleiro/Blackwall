using Blackwall.Infrastructure.Persistence;

namespace Blackwall.Modules.Banlist;

public enum BanPlatform {
    Discord,
    Twitch
}

public interface IBanSyncDataAccess {
    BanPlatform Platform { get; }

    Task<List<long>> GetActivePlatformIdsAsync(BlackwallDbContext db, CancellationToken ct);

    Task<(long InstanceId, List<BanEntry> Bans)?> GetInstanceWithBansAsync(BlackwallDbContext db, long platformId, CancellationToken ct);

    Task<(long InstanceId, bool ShareBanList, List<BanEntry> Bans)?> GetSourceInstanceWithBansAsync(BlackwallDbContext db, long sourcePlatformId, CancellationToken ct);

    Task<(long InstanceId, List<BanEntry> Bans)?> GetTargetInstanceWithBansAsync(BlackwallDbContext db, long targetPlatformId, CancellationToken ct);

    void RemoveBan(BlackwallDbContext db, BanEntry ban);
    void AddBan(BlackwallDbContext db, long instanceId, BanEntry ban);
    void UpdateBan(BlackwallDbContext db, BanEntry existing, PlatformBanRecord updated);
    Task UpdateInstanceTimestampAsync(BlackwallDbContext db, long instanceId, CancellationToken ct);

    Task<List<SyncRuleEntry>> GetEnabledSyncRulesAsync(BlackwallDbContext db, CancellationToken ct);
    Task<(long TargetPlatformId, bool IsActive)?> GetSyncRuleTargetAsync(BlackwallDbContext db, SyncRuleEntry rule, CancellationToken ct);
    Task<(long SourcePlatformId, bool IsActive, bool ShareBanList)?> GetSyncRuleSourceAsync(BlackwallDbContext db, SyncRuleEntry rule, CancellationToken ct);
    Task UpdateSyncRuleTimestampAsync(BlackwallDbContext db, SyncRuleEntry rule, CancellationToken ct);
}

public sealed record BanEntry(
    long Id,
    long UserId,
    string? Username,
    string? Reason,
    DateTime? BannedAtUtc
);

public sealed record SyncRuleEntry(
    long Id,
    long TargetInstanceId,
    long SourcePlatformId
);
