using Blackwall.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Blackwall.Bot.Twitch;

public sealed class TwitchBanSyncBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<TwitchSyncOptions> options,
    ILogger<TwitchBanSyncBackgroundService> logger
) : BackgroundService {
    private readonly TwitchSyncOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        if (!_options.Enabled) {
            logger.LogInformation("Twitch ban sync background service is disabled");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.IntervalMinutes));

        logger.LogInformation(
            "Twitch ban sync background service started with interval {IntervalMinutes} minute(s)",
            interval.TotalMinutes
        );

        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested) {
            try {
                await RunOnceAsync(stoppingToken);
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception ex) {
                logger.LogError(ex, "Error while syncing Twitch bans");
            }

            try {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                    break;
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            }
        }

        logger.LogInformation("Twitch ban sync background service stopped");
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken) {
        using var scope = scopeFactory.CreateScope();
        var banSyncService = scope.ServiceProvider.GetRequiredService<TwitchBanSyncService>();

        await banSyncService.SyncAllBansAsync(cancellationToken);
        await banSyncService.ProcessAutoSyncRulesAsync(cancellationToken);
    }
}
