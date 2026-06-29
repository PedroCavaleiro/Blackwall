using StackExchange.Redis;

namespace Blackwall.Bot.Services;

public sealed class RaidDetectionService(IConnectionMultiplexer redis) {
    private readonly IDatabase _db = redis.GetDatabase();

    /// <summary>
    /// Increments the join counter for the guild and returns whether the raid threshold was
    /// just crossed. Counter resets after <paramref name="windowSeconds"/>.
    /// </summary>
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
    public async Task<bool> IsInLockdownAsync(long discordGuildId) {
        return await _db.KeyExistsAsync($"raid:lockdown:{discordGuildId}");
    }

    /// <summary>
    /// Marks the guild as being in a raid lockdown for <paramref name="cooldownMinutes"/> minutes.
    /// </summary>
    public async Task SetLockdownAsync(long discordGuildId, int cooldownMinutes) {
        await _db.StringSetAsync(
            $"raid:lockdown:{discordGuildId}",
            "1",
            TimeSpan.FromMinutes(cooldownMinutes)
        );
    }
}
