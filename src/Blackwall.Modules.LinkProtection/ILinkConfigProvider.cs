namespace Blackwall.Modules.LinkProtection;

public interface ILinkConfigProvider {
    Task<LinkConfigSnapshot?> LoadConfigAsync(long scopeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<long>> GetAllActiveScopeIdsAsync(CancellationToken cancellationToken = default);
}
