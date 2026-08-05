using System.Text.Json;
using Blackwall.Core.DTOs;
using StackExchange.Redis;

namespace Blackwall.Api.Services.Discord;

public sealed class DiscordGuildCacheService(IConnectionMultiplexer redis) {
    
    private const string KeyPrefix = "user:guilds:";
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(2);
    private readonly IDatabase _db = redis.GetDatabase();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Serializes and stores the guild list for the given user in Redis with a fixed TTL.
    /// </summary>
    /// <param name="appUserId">The application user ID to cache guilds for.</param>
    /// <param name="guilds">The list of Discord guilds to cache.</param>
    public async Task StoreAsync(long appUserId, IReadOnlyList<DiscordGuildDto> guilds) {
        var key = $"{KeyPrefix}{appUserId}";
        var json = JsonSerializer.Serialize(guilds, _jsonOptions);
        await _db.StringSetAsync(key, json, Ttl);
    }

    /// <summary>
    /// Retrieves the cached guild list for the given user from Redis.
    /// </summary>
    /// <param name="appUserId">The application user ID to look up.</param>
    /// <returns>The cached list of Discord guilds, or <c>null</c> if the entry is absent or expired.</returns>
    public async Task<IReadOnlyList<DiscordGuildDto>?> GetAsync(long appUserId) {
        var key = $"{KeyPrefix}{appUserId}";
        var cached = await _db.StringGetAsync(key);

        return !cached.HasValue 
            ? null 
            : JsonSerializer.Deserialize<List<DiscordGuildDto>>((string)cached!, _jsonOptions);
    }
}
