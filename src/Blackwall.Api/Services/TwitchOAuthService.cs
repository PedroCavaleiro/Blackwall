using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Blackwall.Core.Configuration;
using Blackwall.Core.DTOs;
using Blackwall.Core.Entities;
using Blackwall.Core.Services;
using Blackwall.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Blackwall.Api.Services;

public sealed class TwitchOAuthService(
    HttpClient httpClient,
    IConnectionMultiplexer redis,
    IOptions<TwitchOptions> options,
    BlackwallDbContext dbContext,
    IOptions<AppConfiguration> appConfiguration
) {
    private readonly TwitchOptions _options = options.Value;
    private readonly IDatabase _db = redis.GetDatabase();
    private const string OAuthStateKeyPrefix = "oauth:state:twitch:";
    private const string BotInstallStateKeyPrefix = "oauth:state:twitch:bot:";
    private const string TwitchApiBase = "https://api.twitch.tv/helix";
    private const string TwitchOauthBase = "https://id.twitch.tv/oauth2";

    public string BuildLoginUrl(string state) {
        var query = new Dictionary<string, string> {
            ["client_id"] = _options.ClientId,
            ["redirect_uri"] = _options.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = _options.LoginScopes,
            ["state"] = state,
            ["force_verify"] = "false"
        };

        var queryString = string.Join("&",
            query.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));

        return $"{TwitchOauthBase}/authorize?{queryString}";
    }

    public async Task<(string AccessToken, string RefreshToken, DateTime ExpiresAt)> ExchangeCodeAsync(string code, CancellationToken cancellationToken = default) {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{TwitchOauthBase}/token");

        request.Content = new FormUrlEncodedContent(new Dictionary<string, string> {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = _options.RedirectUri
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var root = document.RootElement;

        var accessToken = root.GetProperty("access_token").GetString()
                          ?? throw new InvalidOperationException("Twitch access token was missing.");

        var refreshToken = root.GetProperty("refresh_token").GetString()
                           ?? throw new InvalidOperationException("Twitch refresh token was missing.");

        var expiresInSeconds = root.GetProperty("expires_in").GetInt32();
        var expiresAt = DateTime.UtcNow.AddSeconds(expiresInSeconds);

        return (accessToken, refreshToken, expiresAt);
    }

    public async Task<TwitchUserDto> GetCurrentUserAsync(string accessToken, CancellationToken cancellationToken = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{TwitchApiBase}/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("Client-Id", _options.ClientId);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var dataArray = document.RootElement.GetProperty("data");
        if (dataArray.GetArrayLength() == 0)
            throw new InvalidOperationException("Twitch user payload was empty.");

        var userObj = dataArray[0];
        return new TwitchUserDto(
            userObj.GetProperty("id").GetString() ?? "",
            userObj.GetProperty("login").GetString() ?? "",
            userObj.GetProperty("display_name").GetString() ?? "",
            userObj.TryGetProperty("email", out var emailProp) ? emailProp.GetString() : null,
            userObj.TryGetProperty("profile_image_url", out var imgProp) ? imgProp.GetString() : null
        );
    }

    public async Task<string> EnsureFreshAccessTokenAsync(AppUser user, CancellationToken cancellationToken = default) {
        var key = AesCrypto.GetBytes(appConfiguration.Value.EncryptionKey);
        var iv = AesCrypto.GetBytes(appConfiguration.Value.EncryptionIv);

        if (user.TwitchTokenExpiresAtUtc.HasValue && user.TwitchTokenExpiresAtUtc.Value > DateTime.UtcNow.AddMinutes(5)) {
            return AesCrypto.DecryptString(
                user.TwitchAccessToken ?? throw new InvalidOperationException("User has no Twitch access token stored."),
                key, iv
            );
        }

        var encryptedRefreshToken = user.TwitchRefreshToken
            ?? throw new InvalidOperationException("User has no Twitch refresh token stored.");
        var refreshToken = AesCrypto.DecryptString(encryptedRefreshToken, key, iv);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{TwitchOauthBase}/token");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string> {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var root = document.RootElement;

        var newAccessToken = root.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Twitch access token was missing.");
        var newRefreshToken = root.GetProperty("refresh_token").GetString()
            ?? throw new InvalidOperationException("Twitch refresh token was missing.");
        var expiresInSeconds = root.GetProperty("expires_in").GetInt32();

        user.TwitchAccessToken = AesCrypto.EncryptString(newAccessToken, key, iv);
        user.TwitchRefreshToken = AesCrypto.EncryptString(newRefreshToken, key, iv);
        user.TwitchTokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(expiresInSeconds);

        await dbContext.SaveChangesAsync(cancellationToken);

        return newAccessToken;
    }

    public async Task<string> CreateAsync() {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var state = Convert.ToBase64String(bytes)
                           .Replace("+", "-")
                           .Replace("/", "_")
                           .Replace("=", "");

        await _db.StringSetAsync(
            $"{OAuthStateKeyPrefix}{state}",
            "1",
            TimeSpan.FromMinutes(10)
        );

        return state;
    }

    public async Task<string> CreateWithLinkAsync(long linkToUserId) {
        var state = await CreateAsync();
        await _db.StringSetAsync(
            $"{OAuthStateKeyPrefix}{state}:linkTo",
            linkToUserId.ToString(),
            TimeSpan.FromMinutes(10)
        );
        return state;
    }

    public async Task<long?> ConsumeWithLinkAsync(string state) {
        var linkKey = $"{OAuthStateKeyPrefix}{state}:linkTo";
        var linkValue = await _db.StringGetDeleteAsync(linkKey);
        return linkValue.HasValue ? long.TryParse((string?)linkValue, out var id) ? id : null : null;
    }

    public async Task<bool> ConsumeAsync(string state) {
        var key = $"{OAuthStateKeyPrefix}{state}";
        var exists = await _db.StringGetDeleteAsync(key);
        return exists.HasValue;
    }

    public string BuildBotInstallUrl(string state) {
        var redirectUri = !string.IsNullOrWhiteSpace(_options.BotRedirectUri)
            ? _options.BotRedirectUri
            : _options.RedirectUri;

        var query = new Dictionary<string, string> {
            ["client_id"] = _options.ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = _options.BotScopes,
            ["state"] = state,
            ["force_verify"] = "true"
        };

        var queryString = string.Join("&",
            query.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));

        return $"{TwitchOauthBase}/authorize?{queryString}";
    }

    public async Task<string> CreateBotInstallStateAsync(long appUserId) {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var state = Convert.ToBase64String(bytes)
                           .Replace("+", "-")
                           .Replace("/", "_")
                           .Replace("=", "");

        await _db.StringSetAsync(
            $"{BotInstallStateKeyPrefix}{state}",
            appUserId.ToString(),
            TimeSpan.FromMinutes(10)
        );

        return state;
    }

    public async Task<long?> ConsumeBotInstallStateAsync(string state) {
        var key = $"{BotInstallStateKeyPrefix}{state}";
        var value = await _db.StringGetDeleteAsync(key);
        return value.HasValue ? long.TryParse((string?)value, out var id) ? id : null : null;
    }

    public async Task<(string AccessToken, string RefreshToken, DateTime ExpiresAt)> ExchangeBotCodeAsync(string code, CancellationToken cancellationToken = default) {
        var redirectUri = !string.IsNullOrWhiteSpace(_options.BotRedirectUri)
            ? _options.BotRedirectUri
            : _options.RedirectUri;

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{TwitchOauthBase}/token");

        request.Content = new FormUrlEncodedContent(new Dictionary<string, string> {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var root = document.RootElement;

        var accessToken = root.GetProperty("access_token").GetString()
                          ?? throw new InvalidOperationException("Twitch access token was missing.");

        var refreshToken = root.GetProperty("refresh_token").GetString()
                           ?? throw new InvalidOperationException("Twitch refresh token was missing.");

        var expiresInSeconds = root.GetProperty("expires_in").GetInt32();
        var expiresAt = DateTime.UtcNow.AddSeconds(expiresInSeconds);

        return (accessToken, refreshToken, expiresAt);
    }
}
