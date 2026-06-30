using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Blackwall.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable PropertyCanBeMadeInitOnly.Global
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable UnusedMember.Global
// ReSharper disable CollectionNeverUpdated.Global
// ReSharper disable NullableWarningSuppressionIsUsed

namespace Blackwall.Bot.Services;

/// <summary>
/// Response from hashLists:list containing metadata about available hash lists.
/// </summary>
public sealed class HashListsListResponse {
    [JsonPropertyName("hashLists")]
    public List<HashListMetadata>? HashLists { get; set; }

    [JsonPropertyName("nextPageToken")]
    public string? NextPageToken { get; set; }
}

public sealed class HashListMetadata {
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("metadata")]
    public HashListMetadataInfo? Metadata { get; set; }
}

public sealed class HashListMetadataInfo {
    [JsonPropertyName("threatTypes")]
    public List<string>? ThreatTypes { get; set; }

    [JsonPropertyName("likelySafeTypes")]
    public List<string>? LikelySafeTypes { get; set; }

    [JsonPropertyName("hashLength")]
    public string? HashLength { get; set; }
}

/// <summary>
/// Response from hashLists:batchGet containing the actual hash list data.
/// </summary>
public sealed class HashListsBatchGetResponse {
    [JsonPropertyName("hashLists")]
    public List<HashListData>? HashLists { get; set; }
}

public sealed class HashListData {
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("partialUpdate")]
    public bool PartialUpdate { get; set; }

    [JsonPropertyName("compressedRemovals")]
    public RiceDeltaEncoded32Bit? CompressedRemovals { get; set; }

    [JsonPropertyName("minimumWaitDuration")]
    public string? MinimumWaitDuration { get; set; }

    [JsonPropertyName("metadata")]
    public HashListMetadataInfo? Metadata { get; set; }

    [JsonPropertyName("additionsFourBytes")]
    public RiceDeltaEncoded32Bit? AdditionsFourBytes { get; set; }

    [JsonPropertyName("additionsThirtyTwoBytes")]
    public RiceDeltaEncoded256Bit? AdditionsThirtyTwoBytes { get; set; }
}

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

    private static readonly HttpClient HttpClient = new() {
        Timeout = TimeSpan.FromSeconds(60)
    };

    private readonly IDatabase _db = redis.GetDatabase();

    /// <summary>
    /// Returns true if the initial sync has been completed and the Global Cache is available.
    /// </summary>
    public async Task<bool> IsSyncedAsync() {
        return await _db.KeyExistsAsync(SyncedKey);
    }

    /// <summary>
    /// Checks if a full 32-byte SHA256 hash exists in the Global Cache.
    /// </summary>
    public async Task<bool> IsInGlobalCacheAsync(byte[] fullHash) {
        return await _db.SetContainsAsync(GlobalCacheKey, Convert.ToHexString(fullHash));
    }

    /// <summary>
    /// Checks if a 4-byte hash prefix exists in the threat lists.
    /// </summary>
    public async Task<bool> IsInThreatListAsync(byte[] prefix) {
        return await _db.SetContainsAsync(ThreatPrefixesKey, Convert.ToHexString(prefix));
    }

    /// <summary>
    /// Discovers available hash lists, downloads them, and stores their contents in Redis.
    /// Supports incremental updates when version tokens are already stored.
    /// Returns the minimum wait duration before the next sync should occur.
    /// </summary>
    public async Task<TimeSpan> SyncAsync(CancellationToken cancellationToken = default) {
        var apiKey = options.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey)) {
            logger.LogWarning("Safe Browsing API key is not configured, skipping sync");
            return TimeSpan.FromMinutes(15);
        }

        try {
            var listNames = await DiscoverHashListNamesAsync(apiKey, cancellationToken);
            if (listNames.Count == 0) {
                logger.LogWarning("No hash lists discovered from Safe Browsing API");
                return TimeSpan.FromMinutes(15);
            }

            logger.LogInformation("Discovered {Count} hash lists: {Names}",
                listNames.Count, string.Join(", ", listNames));

            var minWaitDuration = await FetchAndUpdateListsAsync(listNames, apiKey, cancellationToken);

            await _db.StringSetAsync(SyncedKey, "1");

            logger.LogInformation("Safe Browsing hash lists synced successfully. Next sync in {Duration}", minWaitDuration);
            return minWaitDuration;
        } catch (Exception ex) {
            logger.LogError(ex, "Error during Safe Browsing hash list sync");
            return TimeSpan.FromMinutes(5);
        }
    }

    /// <summary>
    /// Enumerates all available hash list names from the Safe Browsing API,
    /// following pagination until all pages are consumed.
    /// </summary>
    private async Task<List<string>> DiscoverHashListNamesAsync(string apiKey, CancellationToken ct) {
        var baseUrl = options.Value.BaseUrl.TrimEnd('/');
        var url = $"{baseUrl}/hashLists?key={Uri.EscapeDataString(apiKey)}";

        var names = new List<string>();
        string? pageToken = null;

        do {
            var requestUrl = url;
            if (pageToken is not null)
                requestUrl += $"&pageToken={Uri.EscapeDataString(pageToken)}";

            using var response = await HttpClient.GetAsync(requestUrl, ct);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<HashListsListResponse>(ct);
            if (body?.HashLists is null) break;

            names.AddRange(body.HashLists.Select(h => h.Name));
            pageToken = body.NextPageToken;
        } while (!string.IsNullOrEmpty(pageToken));

        return names;
    }

    /// <summary>
    /// Fetches hash list data via hashLists:batchGet using stored version tokens for
    /// incremental updates, processes each list, and returns the minimum wait duration.
    /// </summary>
    private async Task<TimeSpan> FetchAndUpdateListsAsync(
        List<string> listNames,
        string apiKey,
        CancellationToken ct
    ) {
        var baseUrl = options.Value.BaseUrl.TrimEnd('/');
        var versions = await GetStoredVersionsAsync(listNames);

        var url = $"{baseUrl}/hashLists:batchGet?key={Uri.EscapeDataString(apiKey)}";
        for (var i = 0; i < listNames.Count; i++) {
            url += $"&names={Uri.EscapeDataString(listNames[i])}";
            if (i < versions.Count && versions[i] is not null)
                url += $"&version={Uri.EscapeDataString(versions[i]!)}";
        }

        using var response = await HttpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<HashListsBatchGetResponse>(ct);
        if (body?.HashLists is null) return TimeSpan.FromMinutes(30);

        var minWait = TimeSpan.FromMinutes(30);

        foreach (var list in body.HashLists) {
            try {
                var waitDuration = await ProcessHashListAsync(list);
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
    private async Task<TimeSpan> ProcessHashListAsync(HashListData list) {
        var isGlobalCache = list.Metadata?.LikelySafeTypes is { Count: > 0 };
        var isThreatList = list.Metadata?.ThreatTypes is { Count: > 0 };
        var redisKey = isGlobalCache ? GlobalCacheKey : ThreatPrefixesKey;

        if (!list.PartialUpdate) {
            logger.LogInformation("Full update for hash list {Name}, clearing existing data", list.Name);
            await _db.KeyDeleteAsync(redisKey);
        }

        if (isGlobalCache && list.AdditionsThirtyTwoBytes is not null) {
            var additions = RiceDeltaDecoder.Decode256Bit(list.AdditionsThirtyTwoBytes);
            if (additions.Count > 0) {
                var hexValues = additions.Select(b => (RedisValue)Convert.ToHexString(b)).ToArray();
                await _db.SetAddAsync(redisKey, hexValues);
                logger.LogInformation("Added {Count} entries to Global Cache", additions.Count);
            }
        } else if (isThreatList && list.AdditionsFourBytes is not null) {
            var additions = RiceDeltaDecoder.Decode32Bit(list.AdditionsFourBytes);
            if (additions.Count > 0) {
                var hexValues = additions.Select(v => (RedisValue)Convert.ToHexString(BitConverter.IsLittleEndian
                    ? BitConverter.GetBytes(v).Reverse().ToArray()
                    : BitConverter.GetBytes(v))).ToArray();
                await _db.SetAddAsync(redisKey, hexValues);
                logger.LogInformation("Added {Count} entries to threat list", additions.Count);
            }
        }

        if (list.CompressedRemovals is not null) {
            var removals = RiceDeltaDecoder.Decode32Bit(list.CompressedRemovals);
            if (removals.Count > 0 && isThreatList) {
                var hexValues = removals.Select(v => (RedisValue)Convert.ToHexString(BitConverter.IsLittleEndian
                    ? BitConverter.GetBytes(v).Reverse().ToArray()
                    : BitConverter.GetBytes(v))).ToArray();
                await _db.SetRemoveAsync(redisKey, hexValues);
                logger.LogInformation("Removed {Count} entries from threat list", removals.Count);
            }
        }

        if (list.Version is not null)
            await _db.StringSetAsync($"{VersionKeyPrefix}{list.Name}", list.Version);

        return ParseDuration(list.MinimumWaitDuration);
    }

    /// <summary>
    /// Retrieves the stored version token for each hash list name from Redis,
    /// returning null for lists that have no stored version yet.
    /// </summary>
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
    private static TimeSpan ParseDuration(string? duration) {
        if (string.IsNullOrWhiteSpace(duration) || !duration.EndsWith('s'))
            return TimeSpan.FromMinutes(30);

        var value = duration[..^1];
        return double.TryParse(value, out var seconds)
            ? TimeSpan.FromSeconds(Math.Max(seconds, 1))
            : TimeSpan.FromMinutes(30);
    }
}
