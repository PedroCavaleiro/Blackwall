using System.Text.RegularExpressions;
using Blackwall.Core.Configuration;
using Blackwall.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
// ReSharper disable NullableWarningSuppressionIsUsed

namespace Blackwall.Bot.Services;

public sealed partial class BlacklistService(
    IConnectionMultiplexer redis,
    BlackwallDbContext dbContext,
    IOptions<BlacklistOptions> options,
    ILogger<BlacklistService> logger
) {
    private const string BlacklistKeyPrefix = "blacklist:guild:";
    private const string DomainKeyPrefix = "blacklist:domains:";
    private const string ModeKeyPrefix = "blacklist:mode:";
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(25);

    private static readonly HttpClient HttpClient = new() {
        Timeout = TimeSpan.FromSeconds(30)
    };

    /// <summary>
    /// Downloads a blacklist file from the specified URL and extracts domains
    /// in AdGuard filter list format using a compiled regex.
    /// </summary>
    /// <param name="url">The URL of the blacklist file to download.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A set of extracted domain names, lowercased and case-insensitive.</returns>
    private static async Task<HashSet<string>> DownloadBlacklistAsync(
        string url,
        CancellationToken cancellationToken = default
    ) {
        using var response = await HttpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in AdGuardDomainPattern().Matches(content)) {
            if (match.Groups[1].Success)
                domains.Add(match.Groups[1].Value.ToLowerInvariant());
        }

        return domains;
    }

    /// <summary>
    /// Refreshes the cached blacklist data for a single guild by downloading all
    /// configured blacklist URLs, merging custom domains, and storing the result
    /// in Redis with a 25-hour TTL.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID to refresh.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task RefreshGuildAsync(
        long discordGuildId,
        CancellationToken cancellationToken = default
    ) {
        var config = await dbContext.GuildInstances
            .Where(x => x.DiscordGuildId == discordGuildId && x.IsActive)
            .Select(x => new {
                x.SpamConfiguration.LinkWhitelistMode,
                BlacklistUrls = x.SpamConfiguration.Blacklists.Select(b => b.Url).ToList(),
                CustomDomains = x.SpamConfiguration.BlacklistDomains.Select(d => d.Domain).ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (config is null) {
            await redis.GetDatabase().KeyDeleteAsync([
                $"{BlacklistKeyPrefix}{discordGuildId}",
                $"{DomainKeyPrefix}{discordGuildId}",
                $"{ModeKeyPrefix}{discordGuildId}"
            ]);
            return;
        }

        var blacklistDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var url in config.BlacklistUrls) {
            try {
                var domains = await DownloadBlacklistAsync(url, cancellationToken);
                blacklistDomains.UnionWith(domains);
            } catch (Exception ex) {
                logger.LogWarning(ex,
                    "Failed to download blacklist {Url} for guild {GuildId}",
                    url, discordGuildId);
            }
        }

        var customDomains = config.CustomDomains
            .Select(d => d.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var db = redis.GetDatabase();

        var blKey = $"{BlacklistKeyPrefix}{discordGuildId}";
        var domKey = $"{DomainKeyPrefix}{discordGuildId}";
        var modeKey = $"{ModeKeyPrefix}{discordGuildId}";

        await db.KeyDeleteAsync([blKey, domKey, modeKey]);

        var batch = db.CreateBatch();
        if (blacklistDomains.Count > 0)
            _ = batch.SetAddAsync(blKey, blacklistDomains.Select(d => (RedisValue)d).ToArray());
        if (customDomains.Count > 0)
            _ = batch.SetAddAsync(domKey, customDomains.Select(d => (RedisValue)d).ToArray());
        _ = batch.StringSetAsync(modeKey, config.LinkWhitelistMode ? "1" : "0");
        _ = batch.KeyExpireAsync(blKey, Ttl);
        _ = batch.KeyExpireAsync(domKey, Ttl);
        _ = batch.KeyExpireAsync(modeKey, Ttl);
        batch.Execute();

        logger.LogInformation(
            "Refreshed blacklists for guild {GuildId}: {BlacklistCount} blacklist domains, {CustomCount} custom domains, whitelist={Whitelist}",
            discordGuildId, blacklistDomains.Count, customDomains.Count, config.LinkWhitelistMode);
    }

    /// <summary>
    /// Refreshes the cached blacklist data for all active guilds that have
    /// blacklists or custom domains configured.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task RefreshAllAsync(CancellationToken cancellationToken = default) {
        var guildIds = await dbContext.GuildInstances
            .Where(x => x.IsActive && (
                x.SpamConfiguration.Blacklists.Count > 0 ||
                x.SpamConfiguration.BlacklistDomains.Count > 0))
            .Select(x => x.DiscordGuildId)
            .ToListAsync(cancellationToken);

        logger.LogInformation("Refreshing blacklists for {Count} guild(s)", guildIds.Count);

        foreach (var guildId in guildIds) {
            try {
                await RefreshGuildAsync(guildId, cancellationToken);
            } catch (Exception ex) {
                logger.LogError(ex,
                    "Failed to refresh blacklists for guild {GuildId}",
                    guildId);
            }
        }
    }

    /// <summary>
    /// Determines whether a link is blocked for the given guild based on the
    /// cached blacklist and whitelist mode. In whitelist mode, only domains in
    /// the custom domain set are allowed; all others are blocked. In blacklist
    /// mode, domains present in the blacklist or custom domain set are blocked.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID to check against.</param>
    /// <param name="url">The URL to evaluate.</param>
    /// <returns><c>true</c> if the link is blocked; otherwise <c>false</c>.</returns>
    public async Task<bool> IsLinkBlockedAsync(long discordGuildId, string url) {
        var host = ExtractHost(url);
        if (host is null)
            return false;

        var db = redis.GetDatabase();
        var modeVal = await db.StringGetAsync($"{ModeKeyPrefix}{discordGuildId}");
        var isWhitelist = modeVal.HasValue && (string)modeVal! == "1";

        var parts = host.Split('.');

        if (isWhitelist) {
            var domKey = $"{DomainKeyPrefix}{discordGuildId}";
            for (var i = 0; i < parts.Length - 1; i++) {
                var candidate = string.Join('.', parts, i, parts.Length - i);
                if (await db.SetContainsAsync(domKey, candidate))
                    return false;
            }
            return true;
        }

        var blKey = $"{BlacklistKeyPrefix}{discordGuildId}";
        var domKey2 = $"{DomainKeyPrefix}{discordGuildId}";
        for (var i = 0; i < parts.Length - 1; i++) {
            var candidate = string.Join('.', parts, i, parts.Length - i);
            if (await db.SetContainsAsync(blKey, candidate))
                return true;
            if (await db.SetContainsAsync(domKey2, candidate))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the list of default blacklist URLs from configuration.
    /// </summary>
    /// <returns>A read-only list of default blacklist URLs.</returns>
    public IReadOnlyList<string> GetDefaultBlacklists() => options.Value.Defaults;

    /// <summary>
    /// Extracts the lowercase hostname from a URL, prepending "https://" if no
    /// scheme is present.
    /// </summary>
    /// <param name="url">The URL or bare domain string.</param>
    /// <returns>The lowercased hostname, or <c>null</c> if the URL is invalid.</returns>
    private static string? ExtractHost(string url) {
        if (!url.Contains("://"))
            url = "https://" + url;

        return !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? null
            : uri.Host.ToLowerInvariant();
    }

    /// <summary>
    /// A compiled regex that matches AdGuard-style domain entries
    /// (e.g. <c>||example.com^</c>) and captures the domain name.
    /// </summary>
    /// <returns>A compiled <see cref="Regex"/> for AdGuard domain patterns.</returns>
    [GeneratedRegex(@"^\|\|([a-zA-Z0-9](?:[a-zA-Z0-9\-]*[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9\-]*[a-zA-Z0-9])?)+)\^", RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex AdGuardDomainPattern();
}
