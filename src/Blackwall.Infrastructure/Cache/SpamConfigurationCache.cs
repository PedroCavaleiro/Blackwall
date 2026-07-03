using System.Text.Json;
using Blackwall.Core.DTOs;
using Blackwall.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
// ReSharper disable NullableWarningSuppressionIsUsed

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
                x.SpamConfiguration.DuplicateWindowSeconds,
                x.SpamConfiguration.DuplicateCrossChannelEnabled,
                x.SpamConfiguration.MentionLimit,
                x.SpamConfiguration.BlockInviteLinks,
                x.SpamConfiguration.BlockSuspiciousLinks,
                x.SpamConfiguration.LinkWhitelistMode,
                x.SpamConfiguration.SafeBrowsingEnabled,
                x.SpamConfiguration.SafeBrowsingBlockUnsure,
                x.SpamConfiguration.IsEnabled,
                x.SpamConfiguration.IsDryRun,
                x.SpamConfiguration.IsTestMode,
                x.SpamConfiguration.LogChannelId,
                x.SpamConfiguration.IsAntiRaidEnabled,
                x.SpamConfiguration.AntiRaidJoinThreshold,
                x.SpamConfiguration.AntiRaidWindowSeconds,
                x.SpamConfiguration.AntiRaidCooldownMinutes,
                x.SpamConfiguration.IsAccountScoringEnabled,
                x.SpamConfiguration.AutoTimeoutMediumRiskOnJoin,
                x.SpamConfiguration.AutoTimeoutHighRiskOnJoin,
                x.SpamConfiguration.AccountScoringTimeoutMinutes,
                x.SpamConfiguration.IsLockedDown,
                x.SpamConfiguration.RateLimitAction,
                x.SpamConfiguration.RateLimitAutoLockdown,
                x.SpamConfiguration.RateLimitTimeoutMinutes,
                x.SpamConfiguration.RateLimitMessageDeleteDays,
                x.SpamConfiguration.DuplicateAction,
                x.SpamConfiguration.DuplicateAutoLockdown,
                x.SpamConfiguration.DuplicateTimeoutMinutes,
                x.SpamConfiguration.DuplicateMessageDeleteDays,
                x.SpamConfiguration.MentionLimitAction,
                x.SpamConfiguration.MentionLimitAutoLockdown,
                x.SpamConfiguration.MentionLimitTimeoutMinutes,
                x.SpamConfiguration.MentionLimitMessageDeleteDays,
                x.SpamConfiguration.InviteLinkAction,
                x.SpamConfiguration.InviteLinkAutoLockdown,
                x.SpamConfiguration.InviteLinkTimeoutMinutes,
                x.SpamConfiguration.InviteLinkMessageDeleteDays,
                x.SpamConfiguration.SuspiciousLinkAction,
                x.SpamConfiguration.SuspiciousLinkAutoLockdown,
                x.SpamConfiguration.SuspiciousLinkTimeoutMinutes,
                x.SpamConfiguration.SuspiciousLinkMessageDeleteDays,
                x.SpamConfiguration.IsContentGuardEnabled,
                x.SpamConfiguration.ContentGuardFuzzyMatching,
                x.SpamConfiguration.ContentGuardInvisibleCharScrubbing,
                x.SpamConfiguration.ContentGuardZalgoBlocking,
                x.SpamConfiguration.ContentGuardCopypastaHashing,
                x.SpamConfiguration.ContentGuardFuzzyThreshold,
                x.SpamConfiguration.ContentGuardZalgoMaxCombining,
                x.SpamConfiguration.ContentGuardCopypastaMinLength,
                x.SpamConfiguration.ContentGuardCopypastaThreshold,
                x.SpamConfiguration.ContentGuardCopypastaWindowSeconds,
                x.SpamConfiguration.ContentGuardAction,
                x.SpamConfiguration.ContentGuardAutoLockdown,
                x.SpamConfiguration.ContentGuardTimeoutMinutes,
                x.SpamConfiguration.ContentGuardMessageDeleteDays
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
