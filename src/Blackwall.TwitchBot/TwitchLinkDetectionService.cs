using System.Text.RegularExpressions;
using Blackwall.Core.Configuration;
using Blackwall.Core.Services;
using Blackwall.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
// ReSharper disable NullableWarningSuppressionIsUsed

namespace Blackwall.TwitchBot;

public sealed partial class TwitchLinkDetectionService(
    IConnectionMultiplexer redis,
    IServiceScopeFactory scopeFactory,
    IOptions<BlacklistOptions> options,
    ILogger<TwitchLinkDetectionService> logger
) {
    private const string RuleKeyPrefix = "twitch:linkrules:";
    private const string ModeKeyPrefix = "twitch:linkmode:";
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(25);

    private static readonly HttpClient HttpClient = new() {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public async Task RefreshChannelAsync(
        long broadcasterId,
        CancellationToken cancellationToken = default
    ) {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BlackwallDbContext>();

        var config = await dbContext.TwitchChannelInstances
            .Where(x => x.TwitchUserId == broadcasterId && x.IsActive)
            .Select(x => new {
                x.Configuration.LinkWhitelistMode,
                BlacklistUrls = x.Configuration.Blacklists.Select(b => b.Url).ToList(),
                DomainRules = x.Configuration.DomainRules.Select(d => d.Rule).ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);

        var db = redis.GetDatabase();
        var ruleKey = $"{RuleKeyPrefix}{broadcasterId}";
        var modeKey = $"{ModeKeyPrefix}{broadcasterId}";

        if (config is null) {
            await db.KeyDeleteAsync([ruleKey, modeKey]);
            return;
        }

        var rules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var url in config.BlacklistUrls) {
            try {
                var domains = await DownloadBlacklistAsync(url, cancellationToken);
                foreach (var domain in domains)
                    rules.Add(NormalizeRule(domain));
            } catch (Exception ex) {
                logger.LogWarning(ex,
                    "Failed to download blacklist {Url} for channel {BroadcasterId}",
                    url, broadcasterId);
            }
        }

        foreach (var rule in config.DomainRules)
            rules.Add(NormalizeRule(rule));

        await db.KeyDeleteAsync([ruleKey, modeKey]);

        var batch = db.CreateBatch();
        if (rules.Count > 0)
            _ = batch.SetAddAsync(ruleKey, rules.Select(r => (RedisValue)r).ToArray());
        _ = batch.StringSetAsync(modeKey, config.LinkWhitelistMode ? "1" : "0");
        _ = batch.KeyExpireAsync(ruleKey, Ttl);
        _ = batch.KeyExpireAsync(modeKey, Ttl);
        batch.Execute();

        logger.LogInformation(
            "Refreshed link rules for channel {BroadcasterId}: {RuleCount} rules, whitelist={Whitelist}",
            broadcasterId, rules.Count, config.LinkWhitelistMode);
    }

    public async Task<bool> IsLinkBlockedAsync(long broadcasterId, string url) {
        var parsed = ParseUrl(url);
        if (parsed is null)
            return false;

        var (host, path) = parsed.Value;
        var db = redis.GetDatabase();

        var modeVal = await db.StringGetAsync($"{ModeKeyPrefix}{broadcasterId}");
        var isWhitelist = modeVal.HasValue && (string)modeVal! == "1";

        var ruleKey = $"{RuleKeyPrefix}{broadcasterId}";

        var hostParts = host.Split('.');
        var pathParts = path.Length == 0 ? [] : path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        var hostCandidates = new List<string>();
        for (var i = 0; i < hostParts.Length - 1; i++) {
            hostCandidates.Add(string.Join('.', hostParts, i, hostParts.Length - i));
        }

        var pathCandidates = new List<string> { "" };
        var built = "";
        for (var i = 0; i < Math.Min(pathParts.Length, 6); i++) {
            built += (i == 0 ? "" : "/") + pathParts[i];
            pathCandidates.Add("/" + built);
        }

        foreach (var hc in hostCandidates) {
            foreach (var pc in pathCandidates) {
                var candidate = $"{hc}|{pc}";
                if (await db.SetContainsAsync(ruleKey, candidate))
                    return isWhitelist ? false : true;
            }
        }

        return isWhitelist;
    }

    public IReadOnlyList<string> GetDefaultBlacklists() => options.Value.Defaults;

    private static string NormalizeRule(string rule) {
        rule = rule.Trim().ToLowerInvariant();

        if (!rule.Contains("://"))
            rule = "https://" + rule;

        if (!Uri.TryCreate(rule, UriKind.Absolute, out var uri))
            return rule;

        var host = uri.Host.ToLowerInvariant().Trim('.');
        var path = uri.AbsolutePath == "/" ? "" : uri.AbsolutePath;
        if (path.Length > 0 && path[0] == '/')
            path = path[1..];

        return path.Length == 0 ? $"{host}|" : $"{host}|/{path}";
    }

    private static (string Host, string Path)? ParseUrl(string url) {
        if (!url.Contains("://"))
            url = "https://" + url;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        var host = uri.Host.ToLowerInvariant().Trim('.');
        var path = uri.AbsolutePath == "/" ? "" : uri.AbsolutePath;
        if (path.Length > 0 && path[0] == '/')
            path = path[1..];

        return (host, path);
    }

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

    [GeneratedRegex(@"^\|\|([a-zA-Z0-9](?:[a-zA-Z0-9\-]*[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9\-]*[a-zA-Z0-9])?)+)\^", RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex AdGuardDomainPattern();
}
