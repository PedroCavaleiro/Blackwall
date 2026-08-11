using Blackwall.Core.Configuration;
using Blackwall.Modules.Banlist;

namespace Blackwall.Bot.Discord.Background;

public sealed class DiscordBanSyncBackgroundService(
    Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopeFactory,
    Microsoft.Extensions.Logging.ILogger<DiscordBanSyncBackgroundService> logger
) : BanSyncBackgroundService<GuildSyncOptions>(
        scopeFactory,
        opts => (opts.Enabled, opts.IntervalMinutes),
        "Discord",
        logger
    );
