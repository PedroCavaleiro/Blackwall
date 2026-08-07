using Blackwall.Infrastructure.Persistence;
using Blackwall.Modules.LinkProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
// ReSharper disable NullableWarningSuppressionIsUsed
// ReSharper disable RedundantAnonymousTypePropertyName

namespace Blackwall.Bot.Twitch;

public sealed class TwitchLinkConfigProvider(
    IServiceScopeFactory scopeFactory
) : ILinkConfigProvider {
    public async Task<LinkConfigSnapshot?> LoadConfigAsync(long scopeId, CancellationToken cancellationToken = default) {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BlackwallDbContext>();

        var config = await dbContext.TwitchChannelInstances
            .Where(x => x.TwitchUserId == scopeId && x.IsActive)
            .Select(x => new {
                LinkWhitelistMode = x.Configuration!.LinkWhitelistMode,
                BlacklistUrls = x.Configuration!.Blacklists.Select(b => b.Url).ToList(),
                CustomRules = x.Configuration!.DomainRules.Select(d => d.Rule).ToList()
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

        return await dbContext.TwitchChannelInstances
            .Where(x => x.IsActive && (
                x.Configuration!.Blacklists.Count > 0 ||
                x.Configuration!.DomainRules.Count > 0))
            .Select(x => x.TwitchUserId)
            .ToListAsync(cancellationToken);
    }
}
