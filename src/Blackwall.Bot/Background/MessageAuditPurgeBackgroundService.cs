using Blackwall.Bot.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Blackwall.Bot.Background;

public sealed class MessageAuditPurgeBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<MessageAuditPurgeBackgroundService> logger
) : BackgroundService {

    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        logger.LogInformation("Message audit purge background service started with interval {Hours}h", Interval.TotalHours);

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
                var auditService = scope.ServiceProvider.GetRequiredService<MessageAuditService>();
                await auditService.PurgeExpiredAsync(stoppingToken);
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception ex) {
                logger.LogError(ex, "Error during message audit purge");
            }
        }

        logger.LogInformation("Message audit purge background service stopped");
    }
}
