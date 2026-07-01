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
    GuildMemberHandler guildMemberHandler,
    InteractionHandler interactionHandler,
    IOptions<DiscordOptions> options,
    ILogger<BotWorker> logger
) : IHostedService {

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken) {
        client.Log += LogAsync;
        client.Ready += OnReadyAsync;
        client.JoinedGuild += guildHandler.OnJoinedGuildAsync;
        client.LeftGuild += guildHandler.OnLeftGuildAsync;
        client.MessageReceived += messageHandler.OnMessageReceivedAsync;
        client.UserJoined += guildMemberHandler.OnUserJoinedAsync;
        client.InteractionCreated += interactionHandler.OnInteractionCreatedAsync;

        await client.LoginAsync(TokenType.Bot, options.Value.BotToken);
        await client.StartAsync();
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken) {
        client.Ready -= OnReadyAsync;
        client.JoinedGuild -= guildHandler.OnJoinedGuildAsync;
        client.LeftGuild -= guildHandler.OnLeftGuildAsync;
        client.MessageReceived -= messageHandler.OnMessageReceivedAsync;
        client.UserJoined -= guildMemberHandler.OnUserJoinedAsync;
        client.InteractionCreated -= interactionHandler.OnInteractionCreatedAsync;

        await client.StopAsync();
        await client.LogoutAsync();
    }

    /// <summary>
    /// Called when the Discord client is fully ready. Re-syncs all guilds the bot is currently
    /// a member of to ensure <see cref="Blackwall.Core.Entities.GuildInstance.IsActive"/> is
    /// correct after a restart or reconnect.
    /// </summary>
    private async Task OnReadyAsync() {
        foreach (var guild in client.Guilds)
            await guildHandler.OnJoinedGuildAsync(guild);

        await client.SetActivityAsync(new Game("blackwall.observer"));
        await client.SetStatusAsync(UserStatus.Online);

        await interactionHandler.RegisterCommandsAsync(client);
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
