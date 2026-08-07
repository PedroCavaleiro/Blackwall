using Blackwall.LinkProtection.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Blackwall.LinkProtection.Background;

public sealed class SafeBrowsingSyncBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<SafeBrowsingSyncBackgroundService> logger
) : BackgroundService {
    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        logger.LogInformation("Safe Browsing sync background service started");

        while (!stoppingToken.IsCancellationRequested) {
            TimeSpan interval;
            try {
                using var scope = scopeFactory.CreateScope();
                var syncService = scope.ServiceProvider.GetRequiredService<SafeBrowsingSyncService>();
                interval = await syncService.SyncAsync(stoppingToken);
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception ex) {
                logger.LogError(ex, "Error during Safe Browsing sync");
                interval = TimeSpan.FromMinutes(5);
            }

            try {
                await Task.Delay(interval, stoppingToken);
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            }
        }

        logger.LogInformation("Safe Browsing sync background service stopped");
    }
}
