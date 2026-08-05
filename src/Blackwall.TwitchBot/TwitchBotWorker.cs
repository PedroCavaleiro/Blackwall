using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Blackwall.TwitchBot;

public sealed class TwitchBotWorker(
    TwitchBotService twitchBotService,
    ILogger<TwitchBotWorker> logger
) : IHostedService {

    public async Task StartAsync(CancellationToken cancellationToken) {
        try {
            await twitchBotService.InitializeAsync(cancellationToken);
            logger.LogInformation("TwitchBot worker started successfully");
        } catch (Exception ex) {
            logger.LogError(ex, "Failed to initialize TwitchBot worker");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken) {
        try {
            await twitchBotService.DisposeAsync();
            logger.LogInformation("TwitchBot worker stopped");
        } catch (Exception ex) {
            logger.LogError(ex, "Error during TwitchBot worker shutdown");
        }
    }
}
