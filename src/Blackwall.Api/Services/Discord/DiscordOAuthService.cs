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

namespace Blackwall.Api.Services.Discord;

public sealed class DiscordOAuthService(
    HttpClient httpClient,
    IConnectionMultiplexer redis,
    IOptions<DiscordOptions> options,
    BlackwallDbContext dbContext,
    IOptions<AppConfiguration> appConfiguration
) {
    private readonly DiscordOptions _options = options.Value;
    private readonly IDatabase _db = redis.GetDatabase();
    private const string OAuthStateKeyPrefix = "oauth:state:";
    private const string DiscordApiBase = "https://discord.com/api/v10";

    private readonly JsonSerializerOptions _serializerOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    /// <summary>
    /// Builds the Discord OAuth2 login URL that will redirect the user to the configured callback.
    /// </summary>
    /// <param name="state">The state value to include in the request for CSRF protection.</param>
    /// <returns>The full Discord authorization URL.</returns>
    public string BuildLoginUrl(string state) {
        var query = new Dictionary<string, string> {
            ["client_id"] = _options.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = _options.RedirectUri,
            ["scope"] = _options.LoginScopes,
            ["state"] = state,
            ["prompt"] = "none"
        };

        var queryString = string.Join("&",
            query.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));

        return $"https://discord.com/oauth2/authorize?{queryString}";
    }

    /// <summary>
    /// Builds the Discord OAuth2 URL used to invite the bot to a server.
    /// </summary>
    /// <param name="guildId">The Discord guild ID to pre-select in the bot invite flow. If not provided, Discord will prompt the user to choose a server.</param>
    /// <returns>The full Discord bot invite URL.</returns>
    public string BuildBotInviteUrl(long? guildId = null) {
        var query = new Dictionary<string, string> {
            ["client_id"] = _options.ClientId,
            ["permissions"] = _options.BotPermissions,
            ["scope"] = _options.BotScopes,
            ["integration_type"] = "0"
        };

        if (guildId.HasValue)
            query["guild_id"] = guildId.Value.ToString();

        var queryString = string.Join("&",
            query.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));

        return $"https://discord.com/oauth2/authorize?{queryString}";
    }

    /// <summary>
    /// Exchanges a Discord OAuth2 authorization code for an access token, refresh token, and calculates its absolute expiration time.
    /// </summary>
    /// <param name="code">The authorization code returned by the Discord redirect after the user authorizes the application.</param>
    /// <param name="cancellationToken">An optional cancellation token to cancel the underlying HTTP request.</param>
    /// <returns>
    /// A tuple containing:
    /// <list type="bullet">
    /// <item><description><c>AccessToken</c>: The token used to authenticate requests to the Discord API.</description></item>
    /// <item><description><c>RefreshToken</c>: The token used to obtain a new access token once the current one expires.</description></item>
    /// <item><description><c>ExpiresAt</c>: The absolute date and time when the access token will expire.</description></item>
    /// </list>
    /// </returns>
    /// <exception cref="HttpRequestException">Thrown when the Discord API responds with a non-success HTTP status code.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the JSON response is successfully parsed but is missing the access token or refresh token properties.</exception>
    public async Task<(string AccessToken, string RefreshToken, DateTime ExpiresAt)> ExchangeCodeAsync(string code, CancellationToken cancellationToken = default)  {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{DiscordApiBase}/oauth2/token");

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
                          ?? throw new InvalidOperationException("Discord access token was missing.");

        var refreshToken = root.GetProperty("refresh_token").GetString()
                           ?? throw new InvalidOperationException("Discord refresh token was missing.");

        // Discord returns the expiry time in seconds
        var expiresInSeconds = root.GetProperty("expires_in").GetInt32();
        var expiresAt = DateTime.UtcNow.AddSeconds(expiresInSeconds);

        return (accessToken, refreshToken, expiresAt);
    }

    /// <summary>
    /// Retrieves the current Discord user using the provided access token.
    /// </summary>
    /// <param name="accessToken">The Discord access token.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The current Discord user.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the user payload is empty.</exception>
    public async Task<DiscordUserDto> GetCurrentUserAsync(string accessToken, CancellationToken cancellationToken = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{DiscordApiBase}/users/@me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var user = await JsonSerializer.DeserializeAsync<DiscordUserDto>(stream, _serializerOptions, cancellationToken: cancellationToken);

        return user ?? throw new InvalidOperationException("Discord user payload was empty.");
    }

    /// <summary>
    /// Retrieves the list of Discord guilds the current user is a member of.
    /// </summary>
    /// <param name="accessToken">The Discord access token.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A read-only list of the current user's guilds.</returns>
    public async Task<IReadOnlyList<DiscordGuildDto>> GetCurrentUserGuildsAsync(string accessToken, CancellationToken cancellationToken = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{DiscordApiBase}/users/@me/guilds");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var guilds = await JsonSerializer.DeserializeAsync<List<DiscordGuildDto>>(stream, _serializerOptions, cancellationToken: cancellationToken);

        return guilds ?? [];
    }

    /// <summary>
    /// Ensures the given user has a valid Discord access token, refreshing it if expired or expiring soon.
    /// Decrypts the stored refresh token, calls the Discord token endpoint, then re-encrypts and persists
    /// the new tokens back to the database.
    /// </summary>
    /// <param name="user">The application user whose token should be validated or refreshed.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The plaintext Discord access token, guaranteed to be valid for at least 5 minutes.</returns>
    /// <exception cref="InvalidOperationException">Thrown when required configuration is missing or tokens are absent.</exception>
    /// <exception cref="HttpRequestException">Thrown when the Discord API responds with a non-success HTTP status code.</exception>
    public async Task<string> EnsureFreshAccessTokenAsync(AppUser user, CancellationToken cancellationToken = default) {
        var key = AesCrypto.GetBytes(appConfiguration.Value.EncryptionKey);
        var iv = AesCrypto.GetBytes(appConfiguration.Value.EncryptionIv);

        if (user.DiscordTokenExpiresAtUtc.HasValue && user.DiscordTokenExpiresAtUtc.Value > DateTime.UtcNow.AddMinutes(5)) {
            return AesCrypto.DecryptString(
                user.DiscordAccessToken ?? throw new InvalidOperationException("User has no access token stored."),
                key, iv
            );
        }

        var encryptedRefreshToken = user.DiscordRefreshToken
            ?? throw new InvalidOperationException("User has no refresh token stored.");
        var refreshToken = AesCrypto.DecryptString(encryptedRefreshToken, key, iv);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{DiscordApiBase}/oauth2/token");
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
            ?? throw new InvalidOperationException("Discord access token was missing.");
        var newRefreshToken = root.GetProperty("refresh_token").GetString()
            ?? throw new InvalidOperationException("Discord refresh token was missing.");
        var expiresInSeconds = root.GetProperty("expires_in").GetInt32();

        user.DiscordAccessToken = AesCrypto.EncryptString(newAccessToken, key, iv);
        user.DiscordRefreshToken = AesCrypto.EncryptString(newRefreshToken, key, iv);
        user.DiscordTokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(expiresInSeconds);

        await dbContext.SaveChangesAsync(cancellationToken);

        return newAccessToken;
    }

    /// <summary>
    /// Creates a cryptographically secure OAuth2 state parameter and stores it in Redis with a 10-minute expiry.
    /// </summary>
    /// <returns>The generated URL-safe state string.</returns>
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

    /// <summary>
    /// Validates and consumes an OAuth2 state parameter by removing it from Redis.
    /// </summary>
    /// <param name="state">The state string to validate.</param>
    /// <returns><c>true</c> if the state was valid and successfully consumed; otherwise <c>false</c>.</returns>
    public async Task<bool> ConsumeAsync(string state) {
        var key = $"{OAuthStateKeyPrefix}{state}";
        var exists = await _db.StringGetDeleteAsync(key);
        return exists.HasValue;
    }
}