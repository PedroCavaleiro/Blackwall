using Blackwall.Core.Entities;
using Blackwall.Infrastructure.Persistence;
using Blackwall.Modules.Banlist;
using Microsoft.EntityFrameworkCore;

namespace Blackwall.Bot.Twitch;

public sealed class TwitchBanPlatformProvider(
    TwitchBotService twitchBotService
) : IBanPlatformProvider {
    public string PlatformName => "Twitch channel";

    public async Task<List<PlatformBanRecord>> FetchBansAsync(long twitchUserId, CancellationToken cancellationToken = default) {
        var bans = await twitchBotService.GetChannelBansAsync(twitchUserId, cancellationToken);
        return bans.Select(b => new PlatformBanRecord(b.UserId, b.Username, b.Reason, b.BannedAtUtc)).ToList();
    }

    public async Task BanUserAsync(long targetTwitchUserId, long userId, string reason, CancellationToken cancellationToken = default) {
        await twitchBotService.BanUserAsync(targetTwitchUserId, userId, reason);
    }
}

public sealed class TwitchBanSyncDataAccess : IBanSyncDataAccess {
    public BanPlatform Platform => BanPlatform.Twitch;

    public async Task<List<long>> GetActivePlatformIdsAsync(BlackwallDbContext db, CancellationToken ct) {
        return await db.TwitchChannelInstances
            .Where(x => x.IsActive)
            .Select(x => x.TwitchUserId)
            .ToListAsync(ct);
    }

    public async Task<(long InstanceId, List<BanEntry> Bans)?> GetInstanceWithBansAsync(BlackwallDbContext db, long platformId, CancellationToken ct) {
        var instance = await db.TwitchChannelInstances
            .Include(x => x.Bans)
            .FirstOrDefaultAsync(x => x.TwitchUserId == platformId, ct);

        if (instance is null)
            return null;

        return (instance.Id, instance.Bans.Select(MapBan).ToList());
    }

    public async Task<(long InstanceId, bool ShareBanList, List<BanEntry> Bans)?> GetSourceInstanceWithBansAsync(BlackwallDbContext db, long sourcePlatformId, CancellationToken ct) {
        var instance = await db.TwitchChannelInstances
            .Include(x => x.Bans)
            .FirstOrDefaultAsync(x => x.TwitchUserId == sourcePlatformId && x.IsActive, ct);

        if (instance is null)
            return null;

        return (instance.Id, instance.ShareBanList, instance.Bans.Select(MapBan).ToList());
    }

    public async Task<(long InstanceId, List<BanEntry> Bans)?> GetTargetInstanceWithBansAsync(BlackwallDbContext db, long targetPlatformId, CancellationToken ct) {
        var instance = await db.TwitchChannelInstances
            .Include(x => x.Bans)
            .FirstOrDefaultAsync(x => x.TwitchUserId == targetPlatformId, ct);

        if (instance is null)
            return null;

        return (instance.Id, instance.Bans.Select(MapBan).ToList());
    }

    public void RemoveBan(BlackwallDbContext db, BanEntry ban) {
        var entity = db.TwitchChannelBans.Find(ban.Id);
        if (entity is not null)
            db.TwitchChannelBans.Remove(entity);
    }

    public void AddBan(BlackwallDbContext db, long instanceId, BanEntry ban) {
        db.TwitchChannelBans.Add(new TwitchChannelBan {
            TwitchChannelInstanceId = instanceId,
            TwitchUserId = ban.UserId,
            Username = ban.Username,
            Reason = ban.Reason,
            BannedAtUtc = ban.BannedAtUtc ?? DateTime.UtcNow
        });
    }

    public void UpdateBan(BlackwallDbContext db, BanEntry existing, PlatformBanRecord updated) {
        var entity = db.TwitchChannelBans.Find(existing.Id);
        if (entity is null)
            return;

        entity.Username = updated.Username;
        entity.Reason = updated.Reason;
        if (updated.BannedAtUtc.HasValue)
            entity.BannedAtUtc = updated.BannedAtUtc;
    }

    public async Task UpdateInstanceTimestampAsync(BlackwallDbContext db, long instanceId, CancellationToken ct) {
        var instance = await db.TwitchChannelInstances.FirstOrDefaultAsync(x => x.Id == instanceId, ct);
        if (instance is not null)
            instance.UpdatedAtUtc = DateTime.UtcNow;
    }

    public async Task<List<SyncRuleEntry>> GetEnabledSyncRulesAsync(BlackwallDbContext db, CancellationToken ct) {
        var rules = await db.TwitchChannelBanSyncRules
            .Where(r => r.IsEnabled)
            .ToListAsync(ct);

        return rules.Select(r => new SyncRuleEntry(r.Id, r.TargetTwitchChannelInstanceId, r.SourceTwitchUserId)).ToList();
    }

    public async Task<(long TargetPlatformId, bool IsActive)?> GetSyncRuleTargetAsync(BlackwallDbContext db, SyncRuleEntry rule, CancellationToken ct) {
        var target = await db.TwitchChannelInstances
            .FirstOrDefaultAsync(x => x.Id == rule.TargetInstanceId && x.IsActive, ct);

        if (target is null)
            return null;

        return (target.TwitchUserId, target.IsActive);
    }

    public async Task<(long SourcePlatformId, bool IsActive, bool ShareBanList)?> GetSyncRuleSourceAsync(BlackwallDbContext db, SyncRuleEntry rule, CancellationToken ct) {
        var source = await db.TwitchChannelInstances
            .FirstOrDefaultAsync(x => x.TwitchUserId == rule.SourcePlatformId && x.IsActive && x.ShareBanList, ct);

        if (source is null)
            return null;

        return (source.TwitchUserId, source.IsActive, source.ShareBanList);
    }

    public async Task UpdateSyncRuleTimestampAsync(BlackwallDbContext db, SyncRuleEntry rule, CancellationToken ct) {
        var entity = await db.TwitchChannelBanSyncRules.FirstOrDefaultAsync(r => r.Id == rule.Id, ct);
        if (entity is not null)
            entity.LastSyncedAtUtc = DateTime.UtcNow;
    }

    private static BanEntry MapBan(TwitchChannelBan ban) => new(ban.Id, ban.TwitchUserId, ban.Username, ban.Reason, ban.BannedAtUtc);
}
