using Blackwall.DiscordBot.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Blackwall.DiscordBot.Background;

public sealed class BlacklistRefreshBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<BlacklistRefreshBackgroundService> logger
) : BackgroundService {

    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        logger.LogInformation("Blacklist refresh background service started with interval {Hours}h", Interval.TotalHours);

        using (var scope = scopeFactory.CreateScope()) {
            var blacklistService = scope.ServiceProvider.GetRequiredService<BlacklistService>();
            try {
                await blacklistService.RefreshAllAsync(stoppingToken);
            } catch (Exception ex) {
                logger.LogError(ex, "Error during initial blacklist refresh");
            }
        }

        using var timer = new PeriodicTimer(Interval);

        while (!stoppingToken.IsCancellationRequested) {
            try {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                    break;
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            }

            try {
                using var scope = scopeFactory.CreateScope();
                var blacklistService = scope.ServiceProvider.GetRequiredService<BlacklistService>();
                await blacklistService.RefreshAllAsync(stoppingToken);
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception ex) {
                logger.LogError(ex, "Error during scheduled blacklist refresh");
            }
        }

        logger.LogInformation("Blacklist refresh background service stopped");
    }
}
