using System.Text.Json;
using Blackwall.Core.Configuration;
using Blackwall.Core.DTOs;
using Blackwall.Core.Services;
using Blackwall.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
// ReSharper disable NullableWarningSuppressionIsUsed

namespace Blackwall.Infrastructure.Cache.Discord;

public sealed class AiSentinelCache(
    IConnectionMultiplexer redis,
    BlackwallDbContext dbContext,
    IOptions<AppConfiguration> appConfiguration
) {
    private const string KeyPrefix = "ai:sentinel:config:";
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);
    private readonly IDatabase _db = redis.GetDatabase();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AiSentinelConfigurationDto?> GetByDiscordGuildIdAsync(
        long discordGuildId,
        CancellationToken cancellationToken = default
    ) {
        var key = $"{KeyPrefix}{discordGuildId}";
        var cached = await _db.StringGetAsync(key);

        if (cached.HasValue)
            return JsonSerializer.Deserialize<AiSentinelConfigurationDto>((string)cached!, _jsonOptions);

        var entity = await dbContext.GuildInstances
            .Where(x => x.DiscordGuildId == discordGuildId && x.IsActive)
            .Select(x => x.AiSentinelConfiguration)
            .FirstOrDefaultAsync(cancellationToken);

        if (entity is null)
            return null;

        var (encKey, encIv) = GetCryptoParams();
        var config = new AiSentinelConfigurationDto(
            entity.IsEnabled,
            entity.IsDryRun,
            entity.IsTrainingMode,
            entity.Provider,
            Decrypt(entity.ApiKey, encKey, encIv),
            entity.OllamaUrl,
            entity.OllamaHeader1Key,
            Decrypt(entity.OllamaHeader1Value, encKey, encIv),
            entity.OllamaHeader2Key,
            Decrypt(entity.OllamaHeader2Value, encKey, encIv),
            entity.OllamaHeader3Key,
            Decrypt(entity.OllamaHeader3Value, encKey, encIv),
            entity.Model,
            entity.Action,
            entity.AutoLockdown,
            entity.TimeoutMinutes,
            entity.MessageDeleteDays
        );

        await _db.StringSetAsync(key, JsonSerializer.Serialize(config, _jsonOptions), Ttl);

        return config;
    }

    public async Task InvalidateAsync(long discordGuildId) {
        await _db.KeyDeleteAsync($"{KeyPrefix}{discordGuildId}");
    }

    private (byte[] Key, byte[] Iv) GetCryptoParams() {
        var key = AesCrypto.GetBytes(appConfiguration.Value.EncryptionKey);
        var iv = AesCrypto.GetBytes(appConfiguration.Value.EncryptionIv);
        return (key, iv);
    }

    private static string? Decrypt(string? cipherText, byte[] key, byte[] iv) {
        if (string.IsNullOrWhiteSpace(cipherText))
            return cipherText;
        try {
            return AesCrypto.DecryptString(cipherText, key, iv);
        } catch {
            return cipherText;
        }
    }
}
