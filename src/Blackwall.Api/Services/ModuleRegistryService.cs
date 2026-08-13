using Blackwall.Core.Configuration;
using Blackwall.Core.DTOs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;

namespace Blackwall.Api.Services;

public sealed class ModuleRegistryService(
    IHttpClientFactory httpClientFactory,
    IOptions<ModulesConfiguration> options,
    ILogger<ModuleRegistryService> logger
) {
    private readonly TimeSpan _cacheTtl = TimeSpan.FromMinutes(Math.Max(1, options.Value.RegistryCacheMinutes));
    private IReadOnlyList<ModuleRegistryEntryDto>? _cachedEntries;
    private DateTime _cacheExpiryUtc = DateTime.MinValue;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<IReadOnlyList<ModuleRegistryEntryDto>> GetRegistryAsync(CancellationToken cancellationToken = default) {
        if (_cachedEntries is not null && DateTime.UtcNow < _cacheExpiryUtc)
            return _cachedEntries;

        await _lock.WaitAsync(cancellationToken);
        try {
            if (_cachedEntries is not null && DateTime.UtcNow < _cacheExpiryUtc)
                return _cachedEntries;

            var url = options.Value.RegistryUrl;
            logger.LogDebug("Fetching module registry from {Url}", url);

            var httpClient = httpClientFactory.CreateClient();
            var index = await httpClient.GetFromJsonAsync<ModuleRegistryIndexDto>(url, cancellationToken);
            _cachedEntries = index?.Modules ?? [];
            _cacheExpiryUtc = DateTime.UtcNow + _cacheTtl;

            logger.LogInformation("Loaded {Count} modules from registry", _cachedEntries.Count);
            return _cachedEntries;
        } catch (Exception ex) {
            logger.LogWarning(ex, "Failed to fetch module registry from {Url}", options.Value.RegistryUrl);
            if (_cachedEntries is not null)
                return _cachedEntries;
            throw;
        } finally {
            _lock.Release();
        }
    }

    public void InvalidateCache() {
        _cachedEntries = null;
        _cacheExpiryUtc = DateTime.MinValue;
    }
}
