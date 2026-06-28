using Blackwall.Bot.Services;
using Blackwall.Infrastructure.Cache;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blackwall.Bot.Handlers;

public sealed class MessageHandler(
    IServiceScopeFactory scopeFactory,
    SpamDetectionService spamDetectionService,
    ILogger<MessageHandler> logger
) {
    /// <summary>
    /// Evaluates an incoming message against the guild's active spam configuration.
    /// Deletes the message if any violation is detected.
    /// </summary>
    public async Task OnMessageReceivedAsync(SocketMessage rawMessage) {
        if (rawMessage is not SocketUserMessage message)
            return;

        if (message.Author.IsBot || message.Author.IsWebhook)
            return;

        if (message.Channel is not SocketGuildChannel guildChannel)
            return;

        var discordGuildId = (long)guildChannel.Guild.Id;
        var discordUserId = (long)message.Author.Id;

        Core.DTOs.SpamConfigurationDto? config;

        using (var scope = scopeFactory.CreateScope()) {
            var cache = scope.ServiceProvider.GetRequiredService<SpamConfigurationCache>();
            config = await cache.GetByDiscordGuildIdAsync(discordGuildId);
        }

        if (config is null)
            return;

        var violations = new List<string>(5);

        if (await spamDetectionService.IsRateLimitedAsync(
                discordGuildId, discordUserId,
                config.MaxMessagesPerWindow, config.RateLimitWindowSeconds))
            violations.Add("rate_limit");

        if (await spamDetectionService.IsDuplicateAsync(
                discordGuildId, discordUserId,
                message.Content, config.DuplicateMessageThreshold, config.RateLimitWindowSeconds))
            violations.Add("duplicate");

        if (SpamDetectionService.ExceedsMentionLimit(message, config.MentionLimit))
            violations.Add("mention_limit");

        if (config.BlockInviteLinks && SpamDetectionService.ContainsInviteLink(message.Content))
            violations.Add("invite_link");

        if (config.BlockSuspiciousLinks && SpamDetectionService.ContainsSuspiciousLink(message.Content))
            violations.Add("suspicious_link");

        if (violations.Count == 0)
            return;

        logger.LogInformation(
            "Spam detected in guild {GuildId} from user {UserId}: {Violations}",
            discordGuildId, discordUserId, string.Join(", ", violations));

        try {
            await message.DeleteAsync();
        } catch (Exception ex) {
            logger.LogWarning(ex,
                "Failed to delete spam message {MessageId} in guild {GuildId}",
                message.Id, discordGuildId);
        }
    }
}
