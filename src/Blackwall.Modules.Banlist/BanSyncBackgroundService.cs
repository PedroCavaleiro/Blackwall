using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Blackwall.Modules.Banlist;

public class BanSyncBackgroundService<TOptions>(
    IServiceScopeFactory scopeFactory,
    Func<TOptions, (bool Enabled, int IntervalMinutes)> optionsSelector,
    string platformName,
    ILogger<BanSyncBackgroundService<TOptions>> logger
) : BackgroundService where TOptions : class {

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var scope = scopeFactory.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<TOptions>>().Value;
        var (enabled, intervalMinutes) = optionsSelector(options);

        if (!enabled) {
            logger.LogInformation("{Platform} ban sync background service is disabled", platformName);
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, intervalMinutes));

        logger.LogInformation(
            "{Platform} ban sync background service started with interval {IntervalMinutes} minute(s)",
            platformName,
            interval.TotalMinutes
        );

        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested) {
            try {
                await RunOnceAsync(stoppingToken);
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception ex) {
                logger.LogError(ex, "Error while syncing {Platform} bans", platformName);
            }

            try {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                    break;
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            }
        }

        logger.LogInformation("{Platform} ban sync background service stopped", platformName);
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken) {
        using var scope = scopeFactory.CreateScope();
        var banSyncService = scope.ServiceProvider.GetRequiredService<BanSyncService>();
        await banSyncService.SyncAllBansAsync(cancellationToken);
        await banSyncService.ProcessAutoSyncRulesAsync(cancellationToken);
    }
}
