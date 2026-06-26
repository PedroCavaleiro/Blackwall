using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Blackwall.Api.Configuration;
using Blackwall.Core.DTOs;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Blackwall.Api.Services;

/// <summary>
/// Provides helpers for Discord OAuth2 flows, including building authorization URLs,
/// exchanging authorization codes for access tokens, and retrieving user information.
/// </summary>
public sealed class DiscordOAuthService(
    HttpClient httpClient,
    IConnectionMultiplexer redis,
    IOptions<DiscordOptions> options
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
    /// <returns>The full Discord bot invite URL.</returns>
    public string BuildBotInviteUrl() {
        var query = new Dictionary<string, string> {
            ["client_id"] = _options.ClientId,
            ["permissions"] = _options.BotPermissions,
            ["scope"] = _options.BotScopes
        };

        var queryString = string.Join("&",
            query.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));

        return $"https://discord.com/oauth2/authorize?{queryString}";
    }

    /// <summary>
    /// Exchanges an OAuth2 authorization code for a Discord access token.
    /// </summary>
    /// <param name="code">The authorization code returned by Discord.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The Discord access token.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the access token is missing from the response.</exception>
    public async Task<string> ExchangeCodeAsync(string code, CancellationToken cancellationToken = default) {
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

        return document.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Discord access token was missing.");
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