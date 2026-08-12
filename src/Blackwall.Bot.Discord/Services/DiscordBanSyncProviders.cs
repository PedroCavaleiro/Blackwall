using Blackwall.Core.Entities;
using Blackwall.Infrastructure.Persistence;
using Blackwall.Modules.Banlist;
using Discord.Rest;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;

namespace Blackwall.Bot.Discord.Services;

public sealed class DiscordBanPlatformProvider(
    DiscordSocketClient discordClient
) : IBanPlatformProvider {
    public string PlatformName => "guild";

    public async Task<List<PlatformBanRecord>> FetchBansAsync(long guildId, CancellationToken cancellationToken = default) {
        var guild = discordClient.GetGuild((ulong)guildId);
        if (guild is null)
            return [];

        var bans = new List<RestBan>();
        await foreach (var batch in guild.GetBansAsync(limit: 1000, options: new() { CancelToken = cancellationToken }).WithCancellation(cancellationToken))
            bans.AddRange(batch);

        return bans.Select(b => new PlatformBanRecord(
            (long)b.User.Id,
            b.User.Username,
            b.Reason,
            null
        )).ToList();
    }

    public async Task BanUserAsync(long targetGuildId, long userId, string reason, CancellationToken cancellationToken = default) {
        var guild = discordClient.GetGuild((ulong)targetGuildId);
        if (guild is null)
            return;

        await guild.AddBanAsync((ulong)userId, pruneDays: 0, reason: reason, options: new() { CancelToken = cancellationToken });
    }
}

public sealed class DiscordBanSyncDataAccess : IBanSyncDataAccess {
    public BanPlatform Platform => BanPlatform.Discord;

    public async Task<List<long>> GetActivePlatformIdsAsync(BlackwallDbContext db, CancellationToken ct) {
        return await db.GuildInstances
            .Where(x => x.IsActive)
            .Select(x => x.DiscordGuildId)
            .ToListAsync(ct);
    }

    public async Task<(long InstanceId, List<BanEntry> Bans)?> GetInstanceWithBansAsync(BlackwallDbContext db, long platformId, CancellationToken ct) {
        var instance = await db.GuildInstances
            .Include(x => x.Bans)
            .FirstOrDefaultAsync(x => x.DiscordGuildId == platformId, ct);

        if (instance is null)
            return null;

        return (instance.Id, instance.Bans.Select(MapBan).ToList());
    }

    public async Task<(long InstanceId, bool ShareBanList, List<BanEntry> Bans)?> GetSourceInstanceWithBansAsync(BlackwallDbContext db, long sourcePlatformId, CancellationToken ct) {
        var instance = await db.GuildInstances
            .Include(x => x.Bans)
            .FirstOrDefaultAsync(x => x.DiscordGuildId == sourcePlatformId && x.IsActive, ct);

        if (instance is null)
            return null;

        return (instance.Id, instance.ShareBanList, instance.Bans.Select(MapBan).ToList());
    }

    public async Task<(long InstanceId, List<BanEntry> Bans)?> GetTargetInstanceWithBansAsync(BlackwallDbContext db, long targetPlatformId, CancellationToken ct) {
        var instance = await db.GuildInstances
            .Include(x => x.Bans)
            .FirstOrDefaultAsync(x => x.DiscordGuildId == targetPlatformId, ct);

        if (instance is null)
            return null;

        return (instance.Id, instance.Bans.Select(MapBan).ToList());
    }

    public void RemoveBan(BlackwallDbContext db, BanEntry ban) {
        var entity = db.GuildBans.Find(ban.Id);
        if (entity is not null)
            db.GuildBans.Remove(entity);
    }

    public void AddBan(BlackwallDbContext db, long instanceId, BanEntry ban) {
        db.GuildBans.Add(new GuildBan {
            GuildInstanceId = instanceId,
            DiscordUserId = ban.UserId,
            Username = ban.Username,
            Reason = ban.Reason,
            BannedAtUtc = ban.BannedAtUtc ?? DateTime.UtcNow
        });
    }

    public void UpdateBan(BlackwallDbContext db, BanEntry existing, PlatformBanRecord updated) {
        var entity = db.GuildBans.Find(existing.Id);
        if (entity is null)
            return;

        entity.Username = updated.Username;
        entity.Reason = updated.Reason;
    }

    public async Task UpdateInstanceTimestampAsync(BlackwallDbContext db, long instanceId, CancellationToken ct) {
        var instance = await db.GuildInstances.FirstOrDefaultAsync(x => x.Id == instanceId, ct);
        if (instance is not null)
            instance.UpdatedAtUtc = DateTime.UtcNow;
    }

    public async Task<List<SyncRuleEntry>> GetEnabledSyncRulesAsync(BlackwallDbContext db, CancellationToken ct) {
        var rules = await db.GuildBanSyncRules
            .Where(r => r.IsEnabled)
            .ToListAsync(ct);

        return rules.Select(r => new SyncRuleEntry(r.Id, r.TargetGuildInstanceId, r.SourceDiscordGuildId)).ToList();
    }

    public async Task<(long TargetPlatformId, bool IsActive)?> GetSyncRuleTargetAsync(BlackwallDbContext db, SyncRuleEntry rule, CancellationToken ct) {
        var target = await db.GuildInstances
            .FirstOrDefaultAsync(x => x.Id == rule.TargetInstanceId && x.IsActive, ct);

        if (target is null)
            return null;

        return (target.DiscordGuildId, target.IsActive);
    }

    public async Task<(long SourcePlatformId, bool IsActive, bool ShareBanList)?> GetSyncRuleSourceAsync(BlackwallDbContext db, SyncRuleEntry rule, CancellationToken ct) {
        var source = await db.GuildInstances
            .FirstOrDefaultAsync(x => x.DiscordGuildId == rule.SourcePlatformId && x.IsActive && x.ShareBanList, ct);

        if (source is null)
            return null;

        return (source.DiscordGuildId, source.IsActive, source.ShareBanList);
    }

    public async Task UpdateSyncRuleTimestampAsync(BlackwallDbContext db, SyncRuleEntry rule, CancellationToken ct) {
        var entity = await db.GuildBanSyncRules.FirstOrDefaultAsync(r => r.Id == rule.Id, ct);
        if (entity is not null)
            entity.LastSyncedAtUtc = DateTime.UtcNow;
    }

    private static BanEntry MapBan(GuildBan ban) => new(ban.Id, ban.DiscordUserId, ban.Username, ban.Reason, ban.BannedAtUtc);
}
