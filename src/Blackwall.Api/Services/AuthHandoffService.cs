using System.Security.Cryptography;
using System.Text.Json;
using Blackwall.Core.Entities;
using StackExchange.Redis;

namespace Blackwall.Api.Services;

public sealed class AuthHandoffService(IConnectionMultiplexer redis) {

    private const string Prefix = "auth:handoff:";
    private readonly IDatabase _db = redis.GetDatabase();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

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

    public async Task<HandoffPayload?> ConsumeAsync(string code) {
        var value = await _db.StringGetDeleteAsync($"{Prefix}{code}");

        return !value.HasValue
            ? null
            : JsonSerializer.Deserialize<HandoffPayload>(value!, _jsonOptions);
    }

    public sealed record HandoffPayload(
        long UserId,
        long DiscordUserId,
        string Username,
        string? DisplayName
    );
}