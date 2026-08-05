using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Blackwall.TwitchBot;

public sealed class TwitchBotWorker(
    ILogger<TwitchBotWorker> logger
) : IHostedService {

    public Task StartAsync(CancellationToken cancellationToken) {
        logger.LogInformation("TwitchBot worker starting — Twitch IRC/EventSub connection will be implemented in a future phase");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) {
        logger.LogInformation("TwitchBot worker stopping");
        return Task.CompletedTask;
    }
}
