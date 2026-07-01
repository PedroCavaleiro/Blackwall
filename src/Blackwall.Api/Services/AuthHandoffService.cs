using System.Security.Cryptography;
using System.Text.Json;
using Blackwall.Core.Entities;
using StackExchange.Redis;

namespace Blackwall.Api.Services;

public sealed class AuthHandoffService(IConnectionMultiplexer redis) {

    private const string Prefix = "auth:handoff:";
    private readonly IDatabase _db = redis.GetDatabase();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Generates a cryptographically secure handoff code for the given user and stores
    /// the associated payload in Redis with a 2-minute expiry.
    /// </summary>
    /// <param name="user">The authenticated user to create a handoff code for.</param>
    /// <returns>A URL-safe handoff code string.</returns>
    public async Task<string> CreateAsync(AppUser user) {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var code = Convert.ToBase64String(bytes)
                          .Replace("+", "-")
                          .Replace("/", "_")
                          .Replace("=", "");

        var payload = JsonSerializer.Serialize(new HandoffPayload(
                                                   user.Id,
                                                   user.DiscordUserId,
                                                   user.Username,
                                                   user.DisplayName), _jsonOptions);

        await _db.StringSetAsync(
            $"{Prefix}{code}",
            payload,
            TimeSpan.FromMinutes(2)
        );

        return code;
    }

    /// <summary>
    /// Validates and consumes a handoff code by atomically removing it from Redis.
    /// </summary>
    /// <param name="code">The handoff code to consume.</param>
    /// <returns>The associated <see cref="HandoffPayload"/> if the code was valid; otherwise <c>null</c>.</returns>
    public async Task<HandoffPayload?> ConsumeAsync(string code) {
        var value = await _db.StringGetDeleteAsync($"{Prefix}{code}");
        
        return !value.HasValue
            ? null
            : JsonSerializer.Deserialize<HandoffPayload>(((string?)value)!, _jsonOptions);
    }

    /// <summary>
    /// Represents the user data stored against a handoff code.
    /// </summary>
    public sealed record HandoffPayload(
        long UserId,
        long DiscordUserId,
        string Username,
        string? DisplayName
    );
}