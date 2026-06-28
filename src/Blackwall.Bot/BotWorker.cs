using Blackwall.Bot.Handlers;
using Blackwall.Core.Configuration;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Blackwall.Bot;

public sealed class BotWorker(
    DiscordSocketClient client,
    GuildHandler guildHandler,
    MessageHandler messageHandler,
    IOptions<DiscordOptions> options,
    ILogger<BotWorker> logger
) : IHostedService {
    
    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken) {
        client.Log += LogAsync;
        client.JoinedGuild += guildHandler.OnJoinedGuildAsync;
        client.LeftGuild += guildHandler.OnLeftGuildAsync;
        client.MessageReceived += messageHandler.OnMessageReceivedAsync;

        await client.LoginAsync(TokenType.Bot, options.Value.BotToken);
        await client.StartAsync();
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken) {
        client.JoinedGuild -= guildHandler.OnJoinedGuildAsync;
        client.LeftGuild -= guildHandler.OnLeftGuildAsync;
        client.MessageReceived -= messageHandler.OnMessageReceivedAsync;

        await client.StopAsync();
        await client.LogoutAsync();
    }

    /// <summary>
    /// Logs a message from the Discord client.
    /// </summary>
    /// <param name="message">The log message.</param>
    private Task LogAsync(LogMessage message) {
        var logLevel = message.Severity switch {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            LogSeverity.Debug => LogLevel.Trace,
            _ => LogLevel.Information
        };

        logger.Log(logLevel, message.Exception, "{Source}: {Message}", message.Source, message.Message);
        return Task.CompletedTask;
    }
}
