using System.Text.Json;
using Blackwall.Core.DTOs;
using StackExchange.Redis;

namespace Blackwall.Api.Services;

public sealed class DiscordGuildCacheService(IConnectionMultiplexer redis) {
    private const string KeyPrefix = "user:guilds:";
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(2);
    private readonly IDatabase _db = redis.GetDatabase();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public async Task StoreAsync(long appUserId, IReadOnlyList<DiscordGuildDto> guilds, CancellationToken cancellationToken = default) {
        var key = $"{KeyPrefix}{appUserId}";
        var json = JsonSerializer.Serialize(guilds, _jsonOptions);
        await _db.StringSetAsync(key, json, Ttl);
    }

    public async Task<IReadOnlyList<DiscordGuildDto>?> GetAsync(long appUserId, CancellationToken cancellationToken = default) {
        var key = $"{KeyPrefix}{appUserId}";
        var cached = await _db.StringGetAsync(key);

        if (!cached.HasValue)
            return null;

        return JsonSerializer.Deserialize<List<DiscordGuildDto>>((string)cached!, _jsonOptions);
    }
}
