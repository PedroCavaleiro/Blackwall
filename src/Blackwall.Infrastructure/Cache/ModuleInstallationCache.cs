using Blackwall.Core.DTOs;
using Blackwall.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Blackwall.Infrastructure.Cache;

public sealed class ModuleInstallationCache(
    IConnectionMultiplexer redis,
    BlackwallDbContext dbContext
) {
    private const string KeyPrefix = "module:installations:";
    private const string SettingsKeyPrefix = "module:settings:";
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);
    private readonly IDatabase _db = redis.GetDatabase();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task<IReadOnlyList<GuildModuleInstallationDto>> GetByDiscordGuildIdAsync(
        long discordGuildId,
        CancellationToken cancellationToken = default
    ) {
        var key = $"{KeyPrefix}{discordGuildId}";
        var cached = await _db.StringGetAsync(key);

        if (cached.HasValue)
            return JsonSerializer.Deserialize<List<GuildModuleInstallationDto>>((string)cached!, _jsonOptions) ?? [];

        var installations = await dbContext.GuildModuleInstallations
            .Where(x => x.GuildInstance.DiscordGuildId == discordGuildId && x.GuildInstance.IsActive && x.IsEnabled)
            .Select(x => new GuildModuleInstallationDto(
                x.Id,
                x.GuildInstance.DiscordGuildId,
                x.ModuleName,
                null,
                x.ModuleVersion,
                x.ModuleAuthor,
                null,
                x.GitUrl,
                x.CanPerformActions,
                x.IsEnabled,
                x.SettingsJson,
                JsonSerializer.Deserialize<BlackwallModuleManifestDto>(x.ManifestJson, _jsonOptions)!
            ))
            .ToListAsync(cancellationToken);

        await _db.StringSetAsync(key, JsonSerializer.Serialize(installations, _jsonOptions), Ttl);
        return installations;
    }

    public async Task InvalidateAsync(long discordGuildId) {
        await _db.KeyDeleteAsync($"{KeyPrefix}{discordGuildId}");
    }
}
