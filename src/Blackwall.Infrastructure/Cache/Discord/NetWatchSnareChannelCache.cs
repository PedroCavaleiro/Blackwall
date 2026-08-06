using System.Text.Json;
using Blackwall.Core.DTOs;
using Blackwall.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
// ReSharper disable NullableWarningSuppressionIsUsed

namespace Blackwall.Infrastructure.Cache.Discord;

public sealed class NetWatchSnareChannelCache(
    IConnectionMultiplexer redis,
    BlackwallDbContext dbContext
) {
    private const string KeyPrefix = "netWatchSnare:channels:";
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);
    private readonly IDatabase _db = redis.GetDatabase();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<NetWatchSnareChannelDto>?> GetByDiscordGuildIdAsync(
        long discordGuildId,
        CancellationToken cancellationToken = default
    ) {
        var key = $"{KeyPrefix}{discordGuildId}";
        var cached = await _db.StringGetAsync(key);

        if (cached.HasValue)
            return JsonSerializer.Deserialize<List<NetWatchSnareChannelDto>>((string)cached!, _jsonOptions);

        var channels = await dbContext.GuildInstances
            .Where(x => x.DiscordGuildId == discordGuildId && x.IsActive)
            .SelectMany(x => x.SpamConfiguration.NetWatchSnareChannels)
            .Select(s => new NetWatchSnareChannelDto(
                s.Id,
                s.DiscordChannelId,
                s.ChannelName,
                s.Action,
                s.TimeoutMinutes,
                s.MessageDeleteDays,
                s.IsEnabled
            ))
            .ToListAsync(cancellationToken);

        await _db.StringSetAsync(key, JsonSerializer.Serialize(channels, _jsonOptions), Ttl);

        return channels;
    }

    public async Task InvalidateAsync(long discordGuildId) {
        await _db.KeyDeleteAsync($"{KeyPrefix}{discordGuildId}");
    }
}
