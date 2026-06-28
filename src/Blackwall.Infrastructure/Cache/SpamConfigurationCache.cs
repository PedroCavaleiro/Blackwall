using System.Text.Json;
using Blackwall.Core.DTOs;
using Blackwall.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Blackwall.Infrastructure.Cache;

public sealed class SpamConfigurationCache(
    IConnectionMultiplexer redis,
    BlackwallDbContext dbContext
) {
    private const string KeyPrefix = "spam:config:";
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);
    private readonly IDatabase _db = redis.GetDatabase();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Retrieves the spam configuration for a guild by its Discord guild ID.
    /// Returns a cached value if available, otherwise loads from the database and caches the result.
    /// Returns null if the guild is not active or has no bot installation.
    /// </summary>
    public async Task<SpamConfigurationDto?> GetByDiscordGuildIdAsync(
        long discordGuildId,
        CancellationToken cancellationToken = default
    ) {
        var key = $"{KeyPrefix}{discordGuildId}";
        var cached = await _db.StringGetAsync(key);

        if (cached.HasValue)
            return JsonSerializer.Deserialize<SpamConfigurationDto>((string)cached!, _jsonOptions);

        var config = await dbContext.GuildInstances
            .Where(x => x.DiscordGuildId == discordGuildId && x.IsActive)
            .Select(x => new SpamConfigurationDto(
                x.SpamConfiguration.MaxMessagesPerWindow,
                x.SpamConfiguration.RateLimitWindowSeconds,
                x.SpamConfiguration.DuplicateMessageThreshold,
                x.SpamConfiguration.MentionLimit,
                x.SpamConfiguration.BlockInviteLinks,
                x.SpamConfiguration.BlockSuspiciousLinks
            ))
            .FirstOrDefaultAsync(cancellationToken);

        if (config is not null)
            await _db.StringSetAsync(key, JsonSerializer.Serialize(config, _jsonOptions), Ttl);

        return config;
    }

    /// <summary>
    /// Removes the cached spam configuration for the given Discord guild ID,
    /// forcing the next read to reload from the database.
    /// </summary>
    public async Task InvalidateAsync(long discordGuildId) {
        await _db.KeyDeleteAsync($"{KeyPrefix}{discordGuildId}");
    }
}
