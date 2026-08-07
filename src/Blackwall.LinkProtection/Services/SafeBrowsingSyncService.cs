using Blackwall.LinkProtection.SafeBrowsingProto;
using Blackwall.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
// ReSharper disable NullableWarningSuppressionIsUsed

namespace Blackwall.LinkProtection.Services;

/// <summary>
/// Metadata about a discovered hash list, used to route list contents during sync.
/// </summary>
public sealed record HashListInfo(string Name, bool IsGlobalCache, bool IsThreatList);

/// <summary>
/// Downloads and synchronizes the Google Safe Browsing V5 Global Cache and threat lists
/// in Redis. Supports incremental updates via version tokens and Rice-Golomb delta encoding.
/// </summary>
public sealed class SafeBrowsingSyncService(
    IConnectionMultiplexer redis,
    IOptions<SafeBrowsingOptions> options,
    ILogger<SafeBrowsingSyncService> logger
) {
    private const string GlobalCacheKey = "sb:globalcache";
    private const string ThreatPrefixesKey = "sb:threats";
    private const string VersionKeyPrefix = "sb:version:";
    private const string SyncedKey = "sb:synced";

    private static readonly HttpClient HttpClient = new(new HttpClientHandler {
        AutomaticDecompression = System.Net.DecompressionMethods.All
    }) {
        Timeout = TimeSpan.FromSeconds(60)
    };

    private readonly IDatabase _db = redis.GetDatabase();

    /// <summary>
    /// Returns true if the initial sync has been completed and the Global Cache is available.
    /// </summary>
    /// <returns><see langword="true"/> if the initial sync has been completed; otherwise <see langword="false"/>.</returns>
    /// <exception cref="RedisException">Thrown when the Redis operation fails.</exception>
    public async Task<bool> IsSyncedAsync() {
        return await _db.KeyExistsAsync(SyncedKey);
    }

    /// <summary>
    /// Checks if a full 32-byte SHA256 hash exists in the Global Cache.
    /// </summary>
    /// <param name="fullHash">The full 32-byte SHA256 hash to check.</param>
    /// <returns><see langword="true"/> if the hash exists in the Global Cache; otherwise <see langword="false"/>.</returns>
    /// <exception cref="RedisException">Thrown when the Redis operation fails.</exception>
    public async Task<bool> IsInGlobalCacheAsync(byte[] fullHash) {
        return await _db.SetContainsAsync(GlobalCacheKey, Convert.ToHexString(fullHash));
    }

    /// <summary>
    /// Checks if a 4-byte hash prefix exists in the threat lists.
    /// </summary>
    /// <param name="prefix">The 4-byte hash prefix to check.</param>
    /// <returns><see langword="true"/> if the prefix exists in the threat lists; otherwise <see langword="false"/>.</returns>
    /// <exception cref="RedisException">Thrown when the Redis operation fails.</exception>
    public async Task<bool> IsInThreatListAsync(byte[] prefix) {
        return await _db.SetContainsAsync(ThreatPrefixesKey, Convert.ToHexString(prefix));
    }

    /// <summary>
    /// Discovers available hash lists, downloads them, and stores their contents in Redis.
    /// Supports incremental updates when version tokens are already stored.
    /// Returns the minimum wait duration before the next sync should occur.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the sync operation.</param>
    /// <returns>The minimum <see cref="TimeSpan"/> to wait before the next sync, or a 5-minute fallback on error.</returns>
    public async Task<TimeSpan> SyncAsync(CancellationToken cancellationToken = default) {
        try {
            return await SyncCoreAsync(cancellationToken);
        } catch (Exception ex) {
            logger.LogError(ex, "Error during Safe Browsing hash list sync");
            return TimeSpan.FromMinutes(5);
        }
    }

    /// <summary>
    /// Performs the sync without swallowing exceptions, so callers can surface errors.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the sync operation.</param>
    /// <returns>The minimum <see cref="TimeSpan"/> to wait before the next sync.</returns>
    /// <exception cref="HttpRequestException">Thrown when an HTTP request to the Safe Browsing API fails.</exception>
    /// <exception cref="RedisException">Thrown when a Redis operation fails.</exception>
    public async Task<TimeSpan> SyncCoreAsync(CancellationToken cancellationToken = default) {
        if (!options.Value.Enabled) {
            logger.LogDebug("Safe Browsing is disabled, skipping sync");
            return TimeSpan.FromHours(1);
        }

        var apiKey = options.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey)) {
            logger.LogWarning("Safe Browsing API key is not configured, skipping sync");
            return TimeSpan.FromMinutes(15);
        }

        var listInfos = await DiscoverHashListNamesAsync(apiKey, cancellationToken);
        if (listInfos.Count == 0) {
            logger.LogWarning("No hash lists discovered from Safe Browsing API");
            return TimeSpan.FromMinutes(15);
        }

        logger.LogInformation("Discovered {Count} hash lists: {Names}",
            listInfos.Count, string.Join(", ", listInfos.Select(l => l.Name)));

        var minWaitDuration = await FetchAndUpdateListsAsync(listInfos, apiKey, cancellationToken);

        await _db.StringSetAsync(SyncedKey, "1");

        logger.LogInformation("Safe Browsing hash lists synced successfully. Next sync in {Duration}", minWaitDuration);
        return minWaitDuration;
    }

    /// <summary>
    /// Enumerates all available hash list names from the Safe Browsing API,
    /// following pagination until all pages are consumed.
    /// </summary>
    /// <param name="apiKey">The Google Safe Browsing API key used for authentication.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>A list of <see cref="HashListInfo"/> records describing each discovered hash list.</returns>
    /// <exception cref="HttpRequestException">Thrown when an HTTP request to the Safe Browsing API fails.</exception>
    private async Task<List<HashListInfo>> DiscoverHashListNamesAsync(string apiKey, CancellationToken ct) {
        var baseUrl = options.Value.BaseUrl.TrimEnd('/');
        var url = $"{baseUrl}/hashLists?key={Uri.EscapeDataString(apiKey)}";

        var infos = new List<HashListInfo>();
        string? pageToken = null;

        do {
            var requestUrl = url;
            if (pageToken is not null)
                requestUrl += $"&pageToken={Uri.EscapeDataString(pageToken)}";

            using var response = await HttpClient.GetAsync(requestUrl, ct);
            if (!response.IsSuccessStatusCode) {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                logger.LogError("Safe Browsing hashLists returned {Status}: {Body}", response.StatusCode, errorBody);
                break;
            }

            var rawBytes = await response.Content.ReadAsByteArrayAsync(ct);
            var body = ListHashListsResponse.Parser.ParseFrom(rawBytes);

            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var list in body.HashLists) {
                var isGlobalCache = list.Metadata.LikelySafeTypes.Count > 0;
                var isThreatList = list.Metadata.ThreatTypes.Count > 0;
                infos.Add(new HashListInfo(list.Name, isGlobalCache, isThreatList));
            }

            pageToken = body.NextPageToken;
        } while (!string.IsNullOrEmpty(pageToken));

        return infos;
    }

    /// <summary>
    /// Fetches hash list data via hashLists:batchGet using stored version tokens for
    /// incremental updates, processes each list, and returns the minimum wait duration.
    /// </summary>
    /// <param name="listInfos">The hash list metadata discovered from the API.</param>
    /// <param name="apiKey">The Google Safe Browsing API key used for authentication.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>The minimum <see cref="TimeSpan"/> to wait before the next sync across all processed lists.</returns>
    /// <exception cref="HttpRequestException">Thrown when an HTTP request to the Safe Browsing API fails.</exception>
    private async Task<TimeSpan> FetchAndUpdateListsAsync(
        List<HashListInfo> listInfos,
        string apiKey,
        CancellationToken ct
    ) {
        var baseUrl = options.Value.BaseUrl.TrimEnd('/');
        var listNames = listInfos.Select(l => l.Name).ToList();
        var versions = await GetStoredVersionsAsync(listNames);

        var url = $"{baseUrl}/hashLists:batchGet?key={Uri.EscapeDataString(apiKey)}";
        for (var i = 0; i < listNames.Count; i++) {
            url += $"&names={Uri.EscapeDataString(listNames[i])}";
            if (i < versions.Count && versions[i] is not null)
                url += $"&version={Uri.EscapeDataString(versions[i]!)}";
        }

        using var response = await HttpClient.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode) {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Safe Browsing batchGet returned {Status}: {Body}", response.StatusCode, errorBody);
            return TimeSpan.FromMinutes(30);
        }

        var rawBytes = await response.Content.ReadAsByteArrayAsync(ct);
        var body = BatchGetHashListsResponse.Parser.ParseFrom(rawBytes);

        var minWait = TimeSpan.FromMinutes(30);
        var infoMap = listInfos.ToDictionary(l => l.Name);

        foreach (var list in body.HashLists) {
            try {
                var info = infoMap.GetValueOrDefault(list.Name);
                var waitDuration = await ProcessHashListAsync(list, info?.IsGlobalCache ?? false, info?.IsThreatList ?? false);
                if (waitDuration < minWait)
                    minWait = waitDuration;
            } catch (Exception ex) {
                logger.LogError(ex, "Error processing hash list {Name}", list.Name);
            }
        }

        return minWait;
    }

    /// <summary>
    /// Processes a single hash list: applies additions and removals to the appropriate
    /// Redis set (Global Cache or threat prefixes), stores the new version token, and
    /// returns the list's minimum wait duration.
    /// </summary>
    /// <param name="list">The hash list payload from the API response.</param>
    /// <param name="isGlobalCache">Whether this list is a Global Cache list (256-bit hashes).</param>
    /// <param name="isThreatList">Whether this list is a threat list (32-bit prefixes).</param>
    /// <returns>The <see cref="TimeSpan"/> minimum wait duration for this list before the next sync.</returns>
    /// <exception cref="RedisException">Thrown when a Redis operation fails.</exception>
    private async Task<TimeSpan> ProcessHashListAsync(HashList list, bool isGlobalCache, bool isThreatList) {
        RedisKey redisKey;
        if (isGlobalCache)
            redisKey = GlobalCacheKey;
        else if (isThreatList)
            redisKey = ThreatPrefixesKey;
        else {
            logger.LogDebug("Skipping unsupported hash list {Name}", list.Name);
            return ParseDuration(list.MinimumWaitDuration);
        }

        if (!list.PartialUpdate) {
            logger.LogInformation("Full update for hash list {Name}, clearing existing data", list.Name);
            await _db.KeyDeleteAsync(redisKey);
        }

        if (isGlobalCache && list.AdditionsThirtyTwoBytes is not null) {
            var additions = RiceDeltaDecoder.Decode256Bit(list.AdditionsThirtyTwoBytes);
            if (additions.Count > 0) {
                var hexValues = additions.Select(b => (RedisValue)Convert.ToHexString(b)).ToArray();
                await BatchSetAddAsync(redisKey, hexValues);
                logger.LogInformation("Added {Count} entries to Global Cache", additions.Count);
            }
        } else if (isThreatList && list.AdditionsFourBytes is not null) {
            var additions = RiceDeltaDecoder.Decode32Bit(list.AdditionsFourBytes);
            if (additions.Count > 0) {
                var hexValues = additions.Select(v => (RedisValue)Convert.ToHexString(BitConverter.IsLittleEndian
                    ? BitConverter.GetBytes(v).Reverse().ToArray()
                    : BitConverter.GetBytes(v))).ToArray();
                await BatchSetAddAsync(redisKey, hexValues);
                logger.LogInformation("Added {Count} entries to threat list", additions.Count);
            }
        }

        if (list.CompressedRemovals is not null) {
            var removals = RiceDeltaDecoder.Decode32Bit(list.CompressedRemovals);
            if (removals.Count > 0 && isThreatList) {
                var hexValues = removals.Select(v => (RedisValue)Convert.ToHexString(BitConverter.IsLittleEndian
                    ? BitConverter.GetBytes(v).Reverse().ToArray()
                    : BitConverter.GetBytes(v))).ToArray();
                await BatchSetRemoveAsync(redisKey, hexValues);
                logger.LogInformation("Removed {Count} entries from threat list", removals.Count);
            }
        }

        if (list.Version.Length > 0)
            await _db.StringSetAsync($"{VersionKeyPrefix}{list.Name}", Convert.ToBase64String(list.Version.ToByteArray()));

        return ParseDuration(list.MinimumWaitDuration);
    }

    /// <summary>
    /// Maximum number of values to send per Redis SADD/SREM call, to stay within
    /// Redis's command argument limit (~1M).
    /// </summary>
    private const int RedisBatchSize = 500_000;

    /// <summary>
    /// Batches SADD calls to stay within Redis's command argument limit (~1M).
    /// </summary>
    /// <param name="key">The Redis set key to add values to.</param>
    /// <param name="values">The values to add to the set.</param>
    /// <exception cref="RedisException">Thrown when a Redis operation fails.</exception>
    private async Task BatchSetAddAsync(RedisKey key, RedisValue[] values) {
        for (var i = 0; i < values.Length; i += RedisBatchSize) {
            var chunk = values[i..Math.Min(i + RedisBatchSize, values.Length)];
            await _db.SetAddAsync(key, chunk);
        }
    }

    /// <summary>
    /// Batches SREM calls to stay within Redis's command argument limit (~1M).
    /// </summary>
    /// <param name="key">The Redis set key to remove values from.</param>
    /// <param name="values">The values to remove from the set.</param>
    /// <exception cref="RedisException">Thrown when a Redis operation fails.</exception>
    private async Task BatchSetRemoveAsync(RedisKey key, RedisValue[] values) {
        for (var i = 0; i < values.Length; i += RedisBatchSize) {
            var chunk = values[i..Math.Min(i + RedisBatchSize, values.Length)];
            await _db.SetRemoveAsync(key, chunk);
        }
    }

    /// <summary>
    /// Retrieves the stored version token for each hash list name from Redis,
    /// returning null for lists that have no stored version yet.
    /// </summary>
    /// <param name="listNames">The names of the hash lists to retrieve version tokens for.</param>
    /// <returns>A list of version token strings, with <see langword="null"/> entries for lists that have no stored version.</returns>
    /// <exception cref="RedisException">Thrown when a Redis operation fails.</exception>
    private async Task<List<string?>> GetStoredVersionsAsync(List<string> listNames) {
        var versions = new List<string?>(listNames.Count);
        foreach (var name in listNames) {
            var val = await _db.StringGetAsync($"{VersionKeyPrefix}{name}");
            versions.Add(val.HasValue ? (string?)val : null);
        }
        return versions;
    }

    /// <summary>
    /// Parses a duration string ending in 's' (e.g. "300s") into a TimeSpan,
    /// falling back to a 30-minute default when parsing fails.
    /// </summary>
    /// <param name="duration">The protobuf <see cref="Duration"/> to parse, or <see langword="null"/>.</param>
    /// <returns>A <see cref="TimeSpan"/> representing the duration, or a 30-minute default if <paramref name="duration"/> is null.</returns>
    private static TimeSpan ParseDuration(Duration? duration) {
        if (duration is null)
            return TimeSpan.FromMinutes(30);

        return TimeSpan.FromSeconds(Math.Max(duration.Seconds, 1))
             + TimeSpan.FromTicks(duration.Nanos / 100);
    }
}
