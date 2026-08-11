namespace Blackwall.Modules.Banlist;

public interface IBanPlatformProvider {
    string PlatformName { get; }

    Task<List<PlatformBanRecord>> FetchBansAsync(long platformId, CancellationToken cancellationToken = default);

    Task BanUserAsync(long targetPlatformId, long userId, string reason, CancellationToken cancellationToken = default);
}
