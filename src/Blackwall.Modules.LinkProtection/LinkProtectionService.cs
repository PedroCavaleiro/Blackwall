using System.Text.RegularExpressions;
using Blackwall.Core.Configuration;
using Blackwall.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
// ReSharper disable NullableWarningSuppressionIsUsed
// ReSharper disable MemberCanBePrivate.Global

namespace Blackwall.Modules.LinkProtection;

public sealed partial class LinkProtectionService(
    IConnectionMultiplexer redis,
    ILinkConfigProvider configProvider,
    IOptions<BlacklistOptions> blacklistOptions,
    LinkProtectionOptions options,
    ILogger<LinkProtectionService> logger
) {
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(25);

    private static readonly HttpClient BlacklistHttpClient = new() {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static readonly HttpClient RedirectHttpClient = new(new HttpClientHandler {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 10
    }) {
        Timeout = TimeSpan.FromSeconds(5)
    };

    private static readonly Regex InviteLinkPattern = InviteLinkPatternRegex();
    private static readonly Regex UrlPattern = UrlPatternRegex();

    public async Task RefreshScopeAsync(
        long scopeId,
        CancellationToken cancellationToken = default
    ) {
        var config = await configProvider.LoadConfigAsync(scopeId, cancellationToken);

        var ruleKey = $"{options.RedisKeyPrefix}:rules:{scopeId}";
        var modeKey = $"{options.RedisKeyPrefix}:mode:{scopeId}";
        var db = redis.GetDatabase();

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
                    "Failed to download blacklist {Url} for scope {ScopeId}",
                    url, scopeId);
            }
        }

        foreach (var rule in config.CustomRules)
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
            "Refreshed link rules for scope {ScopeId}: {RuleCount} rules, whitelist={Whitelist}",
            scopeId, rules.Count, config.LinkWhitelistMode);
    }

    public async Task RefreshAllAsync(CancellationToken cancellationToken = default) {
        var scopeIds = await configProvider.GetAllActiveScopeIdsAsync(cancellationToken);

        logger.LogInformation("Refreshing link rules for {Count} scope(s)", scopeIds.Count);

        foreach (var scopeId in scopeIds) {
            try {
                await RefreshScopeAsync(scopeId, cancellationToken);
            } catch (Exception ex) {
                logger.LogError(ex,
                    "Failed to refresh link rules for scope {ScopeId}",
                    scopeId);
            }
        }
    }

    public async Task<bool> IsLinkBlockedAsync(long scopeId, string url) {
        var parsed = ParseUrl(url);
        if (parsed is null)
            return false;

        var (host, path) = parsed.Value;
        var db = redis.GetDatabase();

        var modeVal = await db.StringGetAsync($"{options.RedisKeyPrefix}:mode:{scopeId}");
        var isWhitelist = modeVal.HasValue && (string)modeVal! == "1";

        var ruleKey = $"{options.RedisKeyPrefix}:rules:{scopeId}";

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

        foreach (var candidate in hostCandidates.SelectMany(hc => pathCandidates.Select(pc => $"{hc}|{pc}"))) {
            if (await db.SetContainsAsync(ruleKey, candidate))
                return !isWhitelist;
        }

        return isWhitelist;
    }

    public IReadOnlyList<string> GetDefaultBlacklists() => blacklistOptions.Value.Defaults;

    public static bool ContainsInviteLink(string content) =>
        InviteLinkPattern.IsMatch(content);

    public static async Task<bool> ContainsInviteLinkWithRedirectAsync(string content) {
        if (ContainsInviteLink(content))
            return true;

        var urls = UrlPattern.Matches(content);
        if (urls.Count == 0)
            return false;

        foreach (Match match in urls) {
            var url = match.Value;

            if (InviteLinkPattern.IsMatch(url))
                return true;

            var redirectUrl = url.Contains("://") ? url : "https://" + url;
            var resolved = await ResolveRedirectAsync(redirectUrl);
            if (resolved is not null && InviteLinkPattern.IsMatch(resolved))
                return true;
        }

        return false;
    }

    public static bool ContainsSuspiciousLink(string content) =>
        SuspiciousLinkPattern().IsMatch(content);

    public async Task<bool> ContainsBlacklistedLinkAsync(string content, long scopeId) {
        var urls = UrlPattern.Matches(content);
        if (urls.Count == 0)
            return false;

        foreach (Match match in urls) {
            var url = match.Value;

            if (await IsLinkBlockedAsync(scopeId, url))
                return true;

            var redirectUrl = url.Contains("://") ? url : "https://" + url;
            var resolved = await ResolveRedirectAsync(redirectUrl);
            if (resolved is not null && await IsLinkBlockedAsync(scopeId, resolved))
                return true;
        }

        return false;
    }

    public static async Task<SafeBrowsingResult> CheckSafeBrowsingAsync(
        string content,
        ISafeBrowsingService safeBrowsingService
    ) {
        var urls = UrlPattern.Matches(content);
        if (urls.Count == 0)
            return SafeBrowsingResult.Safe;

        var worst = SafeBrowsingResult.Safe;

        foreach (Match match in urls) {
            var url = match.Value;

            if (IsDiscordUrl(url))
                continue;

            var result = await safeBrowsingService.CheckUrlAsync(url);
            if (result == SafeBrowsingResult.Unsafe)
                return SafeBrowsingResult.Unsafe;
            if (result == SafeBrowsingResult.Unsure && worst == SafeBrowsingResult.Safe)
                worst = SafeBrowsingResult.Unsure;

            var redirectUrl = url.Contains("://") ? url : "https://" + url;
            var resolved = await ResolveRedirectAsync(redirectUrl);
            if (resolved is not null && !IsDiscordUrl(resolved)) {
                var resolvedResult = await safeBrowsingService.CheckUrlAsync(resolved);
                if (resolvedResult == SafeBrowsingResult.Unsafe)
                    return SafeBrowsingResult.Unsafe;
                if (resolvedResult == SafeBrowsingResult.Unsure && worst == SafeBrowsingResult.Safe)
                    worst = SafeBrowsingResult.Unsure;
            }
        }

        return worst;
    }

    public static IReadOnlyList<string> ExtractUrls(string content) {
        var urls = UrlPattern.Matches(content);
        if (urls.Count == 0)
            return [];

        var result = new List<string>(urls.Count);
        foreach (Match match in urls)
            result.Add(match.Value);
        return result;
    }

    public static bool IsDiscordUrl(string url) {
        if (!url.Contains("://"))
            url = "https://" + url;

        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Host.EndsWith("discord.gg", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith("discord.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith("discordapp.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith("discordapp.net", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<string?> ResolveRedirectAsync(string url) {
        try {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("Blackwall/1.0 (+https://blackwall.app)");

            using var response = await RedirectHttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead);

            return response.RequestMessage?.RequestUri?.ToString();
        } catch {
            return null;
        }
    }

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
        using var response = await BlacklistHttpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in AdGuardDomainPattern().Matches(content)) {
            if (match.Groups[1].Success)
                domains.Add(match.Groups[1].Value.ToLowerInvariant());
        }

        return domains;
    }

    [GeneratedRegex(@"discord(?:\.gg|\.com/invite)/[a-zA-Z0-9-]+", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex InviteLinkPatternRegex();

    [GeneratedRegex(@"https?://(?!(?:cdn\.discordapp\.com|media\.discordapp\.net|discord\.com|discord\.gg))[^\s]+", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex SuspiciousLinkPattern();

    [GeneratedRegex(@"(?:https?://)?[a-zA-Z0-9](?:[a-zA-Z0-9\-]*[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9\-]*[a-zA-Z0-9])?)+(?:/[^\s]*)?", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex UrlPatternRegex();

    [GeneratedRegex(@"^\|\|([a-zA-Z0-9](?:[a-zA-Z0-9\-]*[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9\-]*[a-zA-Z0-9])?)+)\^", RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex AdGuardDomainPattern();
}
