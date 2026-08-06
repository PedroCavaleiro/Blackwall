using System.Security.Cryptography;
using System.Text;
using Blackwall.Core.Configuration;
using Blackwall.Core.Services;
using Blackwall.DiscordBot.Services.SafeBrowsingProto;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
// ReSharper disable NullableWarningSuppressionIsUsed

namespace Blackwall.DiscordBot.Services;

public sealed class SafeBrowsingService(
    IConnectionMultiplexer redis,
    IOptions<SafeBrowsingOptions> options,
    SafeBrowsingSyncService syncService,
    ILogger<SafeBrowsingService> logger
) : ISafeBrowsingService {
    private const string CacheKeyPrefix = "sb:hash:";
    private static readonly TimeSpan DefaultCacheTtl = TimeSpan.FromMinutes(5);

    private static readonly HttpClient HttpClient = new(new HttpClientHandler {
        AutomaticDecompression = System.Net.DecompressionMethods.All
    }) {
        Timeout = TimeSpan.FromSeconds(3)
    };

    private readonly IDatabase _db = redis.GetDatabase();

    /// <summary>
    /// Checks a URL against Google Safe Browsing V5 using the Real-Time Mode procedure.
    /// 1. Check if any full SHA256 hash is in the Global Cache → UNSURE (likely benign)
    /// 2. Check local cache for 4-byte prefixes → UNSAFE if full hash match
    /// 3. Query hashes. Search for remaining prefixes → UNSAFE if match
    /// Returns SAFE, UNSAFE, or UNSURE.
    /// </summary>
    /// <param name="url">The URL to check against the Safe Browsing threat lists.</param>
    /// <returns>A <see cref="SafeBrowsingResult"/> indicating whether the URL is <see cref="SafeBrowsingResult.Safe"/>, <see cref="SafeBrowsingResult.Unsafe"/>, or <see cref="SafeBrowsingResult.Unsure"/>.</returns>
    /// <exception cref="RedisException">Thrown when a Redis cache operation fails unexpectedly.</exception>
    public async Task<SafeBrowsingResult> CheckUrlAsync(string url) {
        if (!options.Value.Enabled) {
            logger.LogDebug("Safe Browsing is disabled, skipping URL check");
            return SafeBrowsingResult.Safe;
        }

        var apiKey = options.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey)) {
            logger.LogWarning("Safe Browsing API key is not configured, skipping URL check");
            return SafeBrowsingResult.Safe;
        }

        var expressions = GenerateHashExpressions(url);
        if (expressions.Count == 0)
            return SafeBrowsingResult.Safe;

        // Step 1: Check Global Cache with full hashes
        if (await syncService.IsSyncedAsync()) {
            foreach (var (_, fullHash) in expressions) {
                if (await syncService.IsInGlobalCacheAsync(fullHash))
                    return SafeBrowsingResult.Unsure;
            }
        } else {
            logger.LogDebug("Global Cache not yet synced, proceeding to hash search");
        }

        // Step 2: Check local cache for each 4-byte prefix
        var prefixesToQuery = new List<(byte[] prefix, byte[] fullHash)>();

        foreach (var (prefix, fullHash) in expressions) {
            var cacheKey = $"{CacheKeyPrefix}{Convert.ToHexString(prefix)}";
            var cached = await _db.StringGetAsync(cacheKey);

            if (cached.HasValue) {
                var cachedStr = (string)cached!;
                if (cachedStr == "safe")
                    continue;

                var cachedHashes = cachedStr.Split(',');
                if (cachedHashes.Contains(Convert.ToHexString(fullHash)))
                    return SafeBrowsingResult.Unsafe;

                continue;
            }

            prefixesToQuery.Add((prefix, fullHash));
        }

        if (prefixesToQuery.Count == 0)
            return SafeBrowsingResult.Safe;

        // Step 3: Query hashes.search for remaining prefixes
        var uniquePrefixes = prefixesToQuery
            .GroupBy(p => Convert.ToHexString(p.prefix))
            .Select(g => g.First())
            .ToList();

        foreach (var (prefix, _) in uniquePrefixes) {
            var serverResult = await QueryHashPrefixAsync(prefix, apiKey);
            var cacheKey = $"{CacheKeyPrefix}{Convert.ToHexString(prefix)}";

            if (serverResult is null) {
                await CacheResultAsync(cacheKey, "unsure", DefaultCacheTtl);
                return SafeBrowsingResult.Unsure;
            }

            var fullHashHexList = serverResult.Value.fullHashes
                .Select(Convert.ToHexString)
                .ToList();

            var cacheValue = fullHashHexList.Count > 0
                ? string.Join(',', fullHashHexList)
                : "safe";

            var ttl = serverResult.Value.cacheDuration;
            if (ttl <= TimeSpan.Zero)
                ttl = DefaultCacheTtl;

            await CacheResultAsync(cacheKey, cacheValue, ttl);

            // Check if any of our expression hashes match
            foreach (var (_, fullHash) in prefixesToQuery) {
                if (Convert.ToHexString(fullHash[..4]) == Convert.ToHexString(prefix)
                    && fullHashHexList.Contains(Convert.ToHexString(fullHash)))
                    return SafeBrowsingResult.Unsafe;
            }
        }

        return SafeBrowsingResult.Safe;
    }

    /// <summary>
    /// Stores a Safe Browsing result in Redis with the specified TTL, logging a warning on failure.
    /// </summary>
    /// <param name="key">The Redis cache key to store the result under.</param>
    /// <param name="value">The value to cache (e.g. a comma-separated list of full hash hex strings, "safe", or "unsure").</param>
    /// <param name="ttl">The time-to-live for the cached entry.</param>
    private async Task CacheResultAsync(string key, string value, TimeSpan ttl) {
        try {
            await _db.StringSetAsync(key, value, ttl);
        } catch (Exception ex) {
            logger.LogWarning(ex, "Failed to cache Safe Browsing result for {Key}", key);
        }
    }

    /// <summary>
    /// Queries the Safe Browsing hashes:search endpoint for a given 4-byte prefix,
    /// returning matching full hashes and the cache duration, or null on error.
    /// </summary>
    /// <param name="prefix">The 4-byte hash prefix to search for.</param>
    /// <param name="apiKey">The Google Safe Browsing API key used for authentication.</param>
    /// <returns>A tuple containing the list of matching full hashes and the cache duration, or <see langword="null"/> if the request fails.</returns>
    private async Task<(List<byte[]> fullHashes, TimeSpan cacheDuration)?> QueryHashPrefixAsync(
        byte[] prefix,
        string apiKey
    ) {
        try {
            var baseUrl = options.Value.BaseUrl.TrimEnd('/');
            var prefixB64 = Convert.ToBase64String(prefix);
            var url = $"{baseUrl}/hashes:search?hashPrefixes={Uri.EscapeDataString(prefixB64)}&key={Uri.EscapeDataString(apiKey)}";

            using var response = await HttpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) {
                logger.LogWarning("Safe Browsing API returned {Status}", response.StatusCode);
                return null;
            }

            var rawBytes = await response.Content.ReadAsByteArrayAsync();
            var body = SearchHashesResponse.Parser.ParseFrom(rawBytes);

            var cacheDuration = ParseDuration(body.CacheDuration);
            var fullHashes = body.FullHashes
                .Select(fh => fh.FullHash_.ToByteArray())
                .ToList();
            return (fullHashes, cacheDuration);
        } catch (Exception ex) {
            logger.LogWarning(ex, "Error querying Safe Browsing API");
            return null;
        }
    }

    /// <summary>
    /// Generates all host-suffix/path-prefix hash expressions for a URL following the
    /// Safe Browsing V5 URL processing procedure. Returns the 4-byte prefix and full SHA256
    /// hash for each expression.
    /// </summary>
    /// <param name="url">The URL to generate hash expressions for.</param>
    /// <returns>A list of tuples containing the 4-byte prefix and full 32-byte SHA256 hash for each expression, or an empty list if the URL is invalid.</returns>
    private static List<(byte[] prefix, byte[] fullHash)> GenerateHashExpressions(string url) {
        if (!url.Contains("://"))
            url = "https://" + url;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return [];

        var host = uri.Host.ToLowerInvariant().Trim('.');
        var path = uri.AbsolutePath == "/" ? "" : uri.AbsolutePath;
        if (path.Length > 0 && path[0] == '/')
            path = path[1..];

        var hostParts = host.Split('.');
        var pathParts = path.Length == 0 ? [] : path.Split('/');

        var hosts = new List<string>();
        if (hostParts.Length <= 4) {
            hosts.Add(host);
        } else {
            for (var i = hostParts.Length - 4; i < hostParts.Length; i++) {
                hosts.Add(string.Join('.', hostParts, i, hostParts.Length - i));
            }
        }

        var paths = new List<string> { "/" };
        if (pathParts.Length > 0) {
            var built = "";
            for (var i = 0; i < Math.Min(pathParts.Length, 4); i++) {
                built += (i == 0 ? "" : "/") + pathParts[i];
                paths.Add("/" + built);
            }
        }

        var expressions = new List<(byte[] prefix, byte[] fullHash)>();
        var seen = new HashSet<string>();

        foreach (var h in hosts) {
            expressions.AddRange(
                from p in paths
                select h + p into expr
                where seen.Add(expr)
                select SHA256.HashData(Encoding.UTF8.GetBytes(expr)) into fullHash
                let prefix = fullHash[..4]
                select (prefix, fullHash)
            );
        }

        return expressions;
    }

    /// <summary>
    /// Parses a protobuf Duration into a TimeSpan,
    /// falling back to the default TTL when the duration is null.
    /// </summary>
    /// <param name="duration">The protobuf <see cref="Duration"/> to parse, or <see langword="null"/>.</param>
    /// <returns>A <see cref="TimeSpan"/> representing the duration, or the default cache TTL if <paramref name="duration"/> is null.</returns>
    private static TimeSpan ParseDuration(Duration? duration) {
        if (duration is null)
            return DefaultCacheTtl;

        return TimeSpan.FromSeconds(Math.Max(duration.Seconds, 1))
             + TimeSpan.FromTicks(duration.Nanos / 100);
    }
}
