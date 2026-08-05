using Blackwall.DiscordBot.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Blackwall.DiscordBot.Background;

public sealed class AiSentinelPurgeBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<AiSentinelPurgeBackgroundService> logger
) : BackgroundService {

    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        logger.LogInformation("AI sentinel purge background service started with interval {Hours}h", Interval.TotalHours);

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
                var aiSentinelService = scope.ServiceProvider.GetRequiredService<AiSentinelService>();
                await aiSentinelService.PurgeExpiredAsync(stoppingToken);
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception ex) {
                logger.LogError(ex, "Error during AI sentinel purge");
            }
        }

        logger.LogInformation("AI sentinel purge background service stopped");
    }
}
