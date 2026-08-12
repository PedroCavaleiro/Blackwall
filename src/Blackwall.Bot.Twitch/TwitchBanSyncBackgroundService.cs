using Blackwall.Core.Configuration;
using Blackwall.Modules.Banlist;

namespace Blackwall.Bot.Twitch;

public sealed class TwitchBanSyncBackgroundService(
    Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopeFactory,
    Microsoft.Extensions.Logging.ILogger<TwitchBanSyncBackgroundService> logger
) : BanSyncBackgroundService<TwitchSyncOptions>(
        scopeFactory,
        opts => (opts.Enabled, opts.IntervalMinutes),
        "Twitch",
        logger
    );
