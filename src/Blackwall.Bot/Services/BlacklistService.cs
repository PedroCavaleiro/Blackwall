using System.Text.RegularExpressions;
using Blackwall.Core.Configuration;
using Blackwall.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

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

    public IReadOnlyList<string> GetDefaultBlacklists() => options.Value.Defaults;

    private static string? ExtractHost(string url) {
        if (!url.Contains("://"))
            url = "https://" + url;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        return uri.Host.ToLowerInvariant();
    }

    [GeneratedRegex(@"^\|\|([a-zA-Z0-9](?:[a-zA-Z0-9\-]*[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9\-]*[a-zA-Z0-9])?)+)\^", RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex AdGuardDomainPattern();
}
