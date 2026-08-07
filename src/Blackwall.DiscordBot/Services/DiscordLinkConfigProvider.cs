using Blackwall.Infrastructure.Persistence;
using Blackwall.Modules.LinkProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Blackwall.DiscordBot.Services;

public sealed class DiscordLinkConfigProvider(
    IServiceScopeFactory scopeFactory
) : ILinkConfigProvider {
    public async Task<LinkConfigSnapshot?> LoadConfigAsync(long scopeId, CancellationToken cancellationToken = default) {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BlackwallDbContext>();

        var config = await dbContext.GuildInstances
            .Where(x => x.DiscordGuildId == scopeId && x.IsActive)
            .Select(x => new {
                x.SpamConfiguration.LinkWhitelistMode,
                BlacklistUrls = x.SpamConfiguration.Blacklists.Select(b => b.Url).ToList(),
                CustomRules = x.SpamConfiguration.BlacklistDomains.Select(d => d.Domain).ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (config is null)
            return null;

        return new LinkConfigSnapshot(
            config.LinkWhitelistMode,
            config.BlacklistUrls,
            config.CustomRules
        );
    }

    public async Task<IReadOnlyList<long>> GetAllActiveScopeIdsAsync(CancellationToken cancellationToken = default) {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BlackwallDbContext>();

        return await dbContext.GuildInstances
            .Where(x => x.IsActive && (
                x.SpamConfiguration.Blacklists.Count > 0 ||
                x.SpamConfiguration.BlacklistDomains.Count > 0))
            .Select(x => x.DiscordGuildId)
            .ToListAsync(cancellationToken);
    }
}
