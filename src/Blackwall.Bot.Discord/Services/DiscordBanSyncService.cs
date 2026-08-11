using Blackwall.Modules.Banlist;

namespace Blackwall.Bot.Discord.Services;

public sealed class DiscordBanSyncService(BanSyncService inner) {
    public Task<int> SyncBansAsync(long discordGuildId, CancellationToken cancellationToken = default)
        => inner.SyncBansAsync(discordGuildId, cancellationToken);

    public Task SyncAllBansAsync(CancellationToken cancellationToken = default)
        => inner.SyncAllBansAsync(cancellationToken);

    public Task ProcessAutoSyncRulesAsync(CancellationToken cancellationToken = default)
        => inner.ProcessAutoSyncRulesAsync(cancellationToken);

    public Task<BanImportResult> ImportBansAsync(
        long targetDiscordGuildId,
        long sourceDiscordGuildId,
        IReadOnlyList<long>? discordUserIds,
        CancellationToken cancellationToken = default)
        => inner.ImportBansAsync(targetDiscordGuildId, sourceDiscordGuildId, discordUserIds, cancellationToken);
}
