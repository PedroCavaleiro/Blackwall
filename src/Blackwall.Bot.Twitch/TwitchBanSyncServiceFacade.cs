using Blackwall.Modules.Banlist;

namespace Blackwall.Bot.Twitch;

public sealed class TwitchBanSyncService(BanSyncService inner) {
    public Task<int> SyncBansAsync(long twitchUserId, CancellationToken cancellationToken = default)
        => inner.SyncBansAsync(twitchUserId, cancellationToken);

    public Task SyncAllBansAsync(CancellationToken cancellationToken = default)
        => inner.SyncAllBansAsync(cancellationToken);

    public Task ProcessAutoSyncRulesAsync(CancellationToken cancellationToken = default)
        => inner.ProcessAutoSyncRulesAsync(cancellationToken);

    public Task<BanImportResult> ImportBansAsync(
        long targetTwitchUserId,
        long sourceTwitchUserId,
        IReadOnlyList<long>? twitchUserIds,
        CancellationToken cancellationToken = default)
        => inner.ImportBansAsync(targetTwitchUserId, sourceTwitchUserId, twitchUserIds, cancellationToken);
}
