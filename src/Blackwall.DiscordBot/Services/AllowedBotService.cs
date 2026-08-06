using Blackwall.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Blackwall.DiscordBot.Services;

public sealed class AllowedBotService(
    IConnectionMultiplexer redis,
    IServiceScopeFactory scopeFactory,
    ILogger<AllowedBotService> logger
) {
    private const string KeyPrefix = "allowedbots:guild:";
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(25);

    public async Task RefreshGuildAsync(
        long discordGuildId,
        CancellationToken cancellationToken = default
    ) {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BlackwallDbContext>();

        var botIds = await dbContext.GuildInstances
            .Where(x => x.DiscordGuildId == discordGuildId && x.IsActive)
            .SelectMany(x => x.SpamConfiguration.AllowedBots.Select(b => b.DiscordBotId))
            .ToListAsync(cancellationToken);

        var key = $"{KeyPrefix}{discordGuildId}";
        var db = redis.GetDatabase();

        await db.KeyDeleteAsync(key);

        if (botIds.Count > 0)
            await db.SetAddAsync(key, botIds.Select(id => (RedisValue)id).ToArray());

        await db.KeyExpireAsync(key, Ttl);

        logger.LogInformation(
            "Refreshed allowed bots for guild {GuildId}: {Count} bot(s)",
            discordGuildId, botIds.Count);
    }

    public async Task<bool> IsBotAllowedAsync(long discordGuildId, long discordBotId) {
        var key = $"{KeyPrefix}{discordGuildId}";
        var db = redis.GetDatabase();

        var exists = await db.KeyExistsAsync(key);
        if (!exists) {
            await RefreshGuildAsync(discordGuildId);
            exists = await db.KeyExistsAsync(key);
        }

        if (!exists)
            return false;

        return await db.SetContainsAsync(key, discordBotId);
    }
}
