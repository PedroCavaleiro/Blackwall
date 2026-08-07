using Blackwall.Bot.Discord.Services;
using Blackwall.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Blackwall.Bot.Discord.Background;

public sealed class BanSyncBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<GuildSyncOptions> options,
    ILogger<BanSyncBackgroundService> logger
) : BackgroundService {
    private readonly GuildSyncOptions _options = options.Value;

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        if (!_options.Enabled) {
            logger.LogInformation("Ban sync background service is disabled");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.IntervalMinutes));

        logger.LogInformation(
            "Ban sync background service started with interval {IntervalMinutes} minute(s)",
            interval.TotalMinutes
        );

        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested) {
            try {
                await RunOnceAsync(stoppingToken);
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception ex) {
                logger.LogError(ex, "Error while syncing bans");
            }

            try {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                    break;
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            }
        }

        logger.LogInformation("Ban sync background service stopped");
    }

    /// <summary>
    /// Runs a single ban sync operation for all active guilds.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    private async Task RunOnceAsync(CancellationToken cancellationToken) {
        using var scope = scopeFactory.CreateScope();
        var banSyncService = scope.ServiceProvider.GetRequiredService<BanSyncService>();

        await banSyncService.SyncAllBansAsync(cancellationToken);
        await banSyncService.ProcessAutoSyncRulesAsync(cancellationToken);
    }
}
