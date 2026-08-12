using Blackwall.Core.DTOs;
using Blackwall.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using System.Text.Json;
using System.Text.Json.Serialization;
// ReSharper disable UnusedMember.Local
// ReSharper disable NullableWarningSuppressionIsUsed

namespace Blackwall.Infrastructure.Cache.Twitch;

public sealed class TwitchModuleInstallationCache(
    IConnectionMultiplexer redis,
    BlackwallDbContext dbContext
) {
    private const string KeyPrefix = "twitch:module:installations:";
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);
    private readonly IDatabase _db = redis.GetDatabase();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task<IReadOnlyList<TwitchChannelModuleInstallationDto>> GetByTwitchUserIdAsync(
        long twitchUserId,
        CancellationToken cancellationToken = default
    ) {
        var key = $"{KeyPrefix}{twitchUserId}";
        var cached = await _db.StringGetAsync(key);

        if (cached.HasValue)
            return JsonSerializer.Deserialize<List<TwitchChannelModuleInstallationDto>>((string)cached!, _jsonOptions) ?? [];

        var installations = await dbContext.TwitchChannelModuleInstallations
            .Where(x => x.TwitchChannelInstance.TwitchUserId == twitchUserId && x.TwitchChannelInstance.IsActive && x.IsEnabled)
            .Select(x => new TwitchChannelModuleInstallationDto(
                x.Id,
                x.TwitchChannelInstance.TwitchUserId,
                x.ModuleName,
                null,
                x.ModuleVersion,
                x.ModuleAuthor,
                null,
                x.GitUrl,
                x.CanPerformActions,
                x.IsEnabled,
                x.DisabledReason,
                x.SettingsJson,
                JsonSerializer.Deserialize<BlackwallModuleManifestDto>(x.ManifestJson, _jsonOptions)!
            ))
            .ToListAsync(cancellationToken);

        await _db.StringSetAsync(key, JsonSerializer.Serialize(installations, _jsonOptions), Ttl);
        return installations;
    }

    public async Task InvalidateAsync(long twitchUserId) {
        await _db.KeyDeleteAsync($"{KeyPrefix}{twitchUserId}");
    }
}
