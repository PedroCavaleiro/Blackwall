using StackExchange.Redis;

namespace Blackwall.Bot.Services;

public sealed class RaidDetectionService(IConnectionMultiplexer redis) {
    private readonly IDatabase _db = redis.GetDatabase();

    /// <summary>
    /// Increments the join counter for the guild and returns whether the raid threshold was
    /// just crossed. Counter resets after <paramref name="windowSeconds"/>.
    /// </summary>
    /// <param name="discordGuildId">The Discord ID of the guild being monitored.</param>
    /// <param name="threshold">The number of joins within the window that triggers a raid.</param>
    /// <param name="windowSeconds">The sliding window in seconds after which the counter resets.</param>
    /// <returns><see langword="true"/> if the join count has reached or exceeded <paramref name="threshold"/>; otherwise <see langword="false"/>.</returns>
    /// <exception cref="RedisException">Thrown when the Redis operation fails.</exception>
    public async Task<bool> RecordJoinAsync(long discordGuildId, int threshold, int windowSeconds) {
        var key = $"raid:joins:{discordGuildId}";
        var count = await _db.StringIncrementAsync(key);

        if (count == 1)
            await _db.KeyExpireAsync(key, TimeSpan.FromSeconds(windowSeconds));

        return count >= threshold;
    }

    /// <summary>
    /// Returns true if the guild is currently in a raid lockdown period.
    /// </summary>
    /// <param name="discordGuildId">The Discord ID of the guild to check.</param>
    /// <returns><see langword="true"/> if the guild is in lockdown; otherwise <see langword="false"/>.</returns>
    /// <exception cref="RedisException">Thrown when the Redis operation fails.</exception>
    public async Task<bool> IsInLockdownAsync(long discordGuildId) {
        return await _db.KeyExistsAsync($"raid:lockdown:{discordGuildId}");
    }

    /// <summary>
    /// Marks the guild as being in a raid lockdown for <paramref name="cooldownMinutes"/> minutes.
    /// </summary>
    /// <param name="discordGuildId">The Discord ID of the guild to mark as locked down.</param>
    /// <param name="cooldownMinutes">The duration in minutes for which the lockdown remains active.</param>
    /// <exception cref="RedisException">Thrown when the Redis operation fails.</exception>
    public async Task SetLockdownAsync(long discordGuildId, int cooldownMinutes) {
        await _db.StringSetAsync(
            $"raid:lockdown:{discordGuildId}",
            "1",
            TimeSpan.FromMinutes(cooldownMinutes)
        );
    }
}
