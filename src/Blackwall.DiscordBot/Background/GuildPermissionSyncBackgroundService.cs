using Blackwall.DiscordBot.Services;
using Blackwall.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Blackwall.DiscordBot.Background;

public sealed class GuildPermissionSyncBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<GuildSyncOptions> options,
    ILogger<GuildPermissionSyncBackgroundService> logger
) : BackgroundService {
    private readonly GuildSyncOptions _options = options.Value;

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        if (!_options.Enabled) {
            logger.LogInformation("Guild permission sync background service is disabled");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.IntervalMinutes));

        logger.LogInformation(
            "Guild permission sync background service started with interval {IntervalMinutes} minute(s)",
            interval.TotalMinutes
        );

        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested) {
            try {
                await RunOnceAsync(stoppingToken);
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception ex) {
                logger.LogError(ex, "Error while syncing guild permissions");
            }

            try {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                    break;
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            }
        }

        logger.LogInformation("Guild permission sync background service stopped");
    }

    /// <summary>
    /// Runs a single sync operation.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    private async Task RunOnceAsync(CancellationToken cancellationToken) {
        using var scope = scopeFactory.CreateScope();
        var syncService = scope.ServiceProvider.GetRequiredService<GuildPermissionSyncService>();

        await syncService.SyncAsync(cancellationToken);
    }
}