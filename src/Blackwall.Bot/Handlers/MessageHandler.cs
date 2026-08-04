using Blackwall.Bot.Services;
using Blackwall.Core.Configuration;
using Blackwall.Core.DTOs;
using Blackwall.Core.Entities;
using Blackwall.Infrastructure.Cache;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;

namespace Blackwall.Bot.Handlers;

public sealed class MessageHandler(
    IServiceScopeFactory scopeFactory,
    SpamDetectionService spamDetectionService,
    LockdownService lockdownService,
    SafeBrowsingService safeBrowsingService,
    ContentGuardService contentGuardService,
    AllowedBotService allowedBotService,
    MessageAuditService messageAuditService,
    NetWatchSnareService netWatchSnareService,
    AiSentinelService aiSentinelService,
    ModuleRunnerService moduleRunnerService,
    DiscordSocketClient discordClient,
    IOptions<AppConfiguration> appConfiguration,
    ILogger<MessageHandler> logger
) {
    private const long DebugLogGuildId = 1194456258703544431;

    /// <summary>
    /// Evaluates an incoming message against the guild's active spam configuration.
    /// Applies the configured infraction action (or dry-runs it) and logs to the configured channel.
    /// </summary>
    public async Task OnMessageReceivedAsync(SocketMessage rawMessage) {
        if (rawMessage is not SocketUserMessage message)
            return;

        if (message.Channel is not SocketGuildChannel guildChannel)
            return;

        var discordGuildId = (long)guildChannel.Guild.Id;
        var discordUserId = (long)message.Author.Id;

        if (discordGuildId == DebugLogGuildId)
            LogDebugMessage(message, guildChannel);

        try {
            await ProcessMessageAsync(message, guildChannel, discordGuildId, discordUserId);
        } catch (Exception ex) {
            logger.LogError(ex,
                "Unhandled exception processing message {MessageId} in guild {GuildId} from user {UserId}",
                message.Id, discordGuildId, discordUserId);
        }
    }

    private async Task ProcessMessageAsync(
        SocketUserMessage message,
        SocketGuildChannel guildChannel,
        long discordGuildId,
        long discordUserId
    ) {
        SpamConfigurationDto? config;

        using (var scope = scopeFactory.CreateScope()) {
            var cache = scope.ServiceProvider.GetRequiredService<SpamConfigurationCache>();
            config = await cache.GetByDiscordGuildIdAsync(discordGuildId);
        }

        if (config is null) {
            logger.LogWarning("Spam config is null for guild {GuildId} — messages will not be checked", discordGuildId);
            return;
        }

        if (!config.IsEnabled) {
            logger.LogWarning("Spam protection is disabled for guild {GuildId}", discordGuildId);
            return;
        }

        if (message.Author.IsWebhook) {
            logger.LogDebug("Ignoring webhook message {MessageId} in guild {GuildId}", message.Id, discordGuildId);
            return;
        }

        if (message.Author.Id == discordClient.CurrentUser.Id)
            return;

        if (message.Author.IsBot && await allowedBotService.IsBotAllowedAsync(discordGuildId, discordUserId)) {
            logger.LogDebug("Ignoring allowed bot {UserId} in guild {GuildId}", discordUserId, discordGuildId);
            return;
        }

        var netWatchSnare = await netWatchSnareService.GetTriggeredNetWatchSnareAsync(discordGuildId, message.Channel.Id);
        if (netWatchSnare is not null) {
            logger.LogInformation(
                "NetWatchSnare trap triggered in guild {GuildId} channel {ChannelId} by user {UserId}",
                discordGuildId, message.Channel.Id, discordUserId);

            try {
                await message.DeleteAsync();
            } catch (Exception ex) {
                logger.LogWarning(ex,
                    "Failed to delete netWatchSnare-triggered message {MessageId} in guild {GuildId}",
                    message.Id, discordGuildId);
            }

            if (!config.IsDryRun)
                await netWatchSnareService.ApplyNetWatchSnareActionAsync(message, guildChannel.Guild, netWatchSnare);

            if (config.LogChannelId.HasValue)
                await netWatchSnareService.SendNetWatchSnareLogAsync(message, guildChannel.Guild, netWatchSnare, config.LogChannelId);

            if (config.IsMessageAuditEnabled) {
                _ = Task.Run(() => messageAuditService.RecordEventAsync(
                    discordGuildId,
                    message,
                    ["NetWatchSnare_trap"],
                    netWatchSnare.Action,
                    config.IsDryRun,
                    config.MessageAuditRetentionDays,
                    null,
                    CancellationToken.None
                ));
            }

            return;
        }

        var violations = new List<string>(5);
        var triggeredModules = new List<(InfractionAction Action, int TimeoutMinutes, int DeleteDays)>(5);
        var shouldLockdown = false;
        List<(ulong ChannelId, ulong MessageId)>? duplicateMessagesToDelete = null;

        if (await spamDetectionService.IsRateLimitedAsync(
                discordGuildId, discordUserId, message.Id,
                config.MaxMessagesPerWindow, config.RateLimitWindowSeconds)) {
            violations.Add("rate_limit");
            triggeredModules.Add((config.RateLimitAction, config.RateLimitTimeoutMinutes, config.RateLimitMessageDeleteDays));
            if (config is { RateLimitAutoLockdown: true, IsLockedDown: false })
                shouldLockdown = true;
        }

        var fullContent = SpamDetectionService.ExtractFullContent(message);

        var dupResult = await spamDetectionService.IsDuplicateAsync(
            discordGuildId, discordUserId,
            message.Channel.Id,
            message.Id,
            fullContent, config.DuplicateMessageThreshold,
            config.DuplicateWindowSeconds, config.DuplicateCrossChannelEnabled);

        if (dupResult.IsDuplicate) {
            violations.Add("duplicate");
            triggeredModules.Add((config.DuplicateAction, config.DuplicateTimeoutMinutes, config.DuplicateMessageDeleteDays));
            if (config is { DuplicateAutoLockdown: true, IsLockedDown: false })
                shouldLockdown = true;
            duplicateMessagesToDelete = dupResult.MessagesToDelete.ToList();
        }

        if (SpamDetectionService.ExceedsMentionLimit(message, config.MentionLimit)) {
            violations.Add("mention_limit");
            triggeredModules.Add((config.MentionLimitAction, config.MentionLimitTimeoutMinutes, config.MentionLimitMessageDeleteDays));
            if (config is { MentionLimitAutoLockdown: true, IsLockedDown: false })
                shouldLockdown = true;
        }

        if (config.BlockInviteLinks && await SpamDetectionService.ContainsInviteLinkWithRedirectAsync(message.Content)) {
            violations.Add("invite_link");
            triggeredModules.Add((config.InviteLinkAction, config.InviteLinkTimeoutMinutes, config.InviteLinkMessageDeleteDays));
            if (config is { InviteLinkAutoLockdown: true, IsLockedDown: false })
                shouldLockdown = true;
        }

        if (config.IsContentGuardEnabled) {
            var cgViolations = await contentGuardService.EvaluateAsync(
                fullContent, discordGuildId, discordUserId, config);

            if (cgViolations.Count > 0) {
                violations.AddRange(cgViolations);
                triggeredModules.Add((config.ContentGuardAction, config.ContentGuardTimeoutMinutes, config.ContentGuardMessageDeleteDays));
                if (config is { ContentGuardAutoLockdown: true, IsLockedDown: false })
                    shouldLockdown = true;
            }
        }

        if (config.BlockSuspiciousLinks) {
            using var blacklistScope = scopeFactory.CreateScope();
            var blacklistService = blacklistScope.ServiceProvider.GetRequiredService<BlacklistService>();

            var blockedByBlacklist = await SpamDetectionService.ContainsBlacklistedLinkAsync(message.Content, blacklistService, discordGuildId);

            if (blockedByBlacklist) {
                violations.Add("suspicious_link");
                triggeredModules.Add((config.SuspiciousLinkAction, config.SuspiciousLinkTimeoutMinutes, config.SuspiciousLinkMessageDeleteDays));
                if (config is { SuspiciousLinkAutoLockdown: true, IsLockedDown: false })
                    shouldLockdown = true;
            } else if (config.SafeBrowsingEnabled) {
                var sbResult = await SpamDetectionService.CheckSafeBrowsingAsync(message.Content, safeBrowsingService);

                if (sbResult == SafeBrowsingResult.Unsafe
                    || (sbResult == SafeBrowsingResult.Unsure && config.SafeBrowsingBlockUnsure)) {
                    violations.Add("safe_browsing");
                    triggeredModules.Add((config.SuspiciousLinkAction, config.SuspiciousLinkTimeoutMinutes, config.SuspiciousLinkMessageDeleteDays));
                    if (config is { SuspiciousLinkAutoLockdown: true, IsLockedDown: false })
                        shouldLockdown = true;
                }
            }
        }

        var moduleResults = await moduleRunnerService.EvaluateAsync(
            discordGuildId, message, guildChannel, config.IsDryRun, CancellationToken.None);

        foreach (var result in moduleResults) {
            violations.Add($"module:{result.ViolationType}");
            triggeredModules.Add((result.Action, result.TimeoutMinutes, result.DeleteDays));
            if (result.AutoLockdown && !config.IsLockedDown)
                shouldLockdown = true;
        }

        if (violations.Count == 0 && appConfiguration.Value.AiSentinelEnabled) {
            await RunAiSentinelAsync(message, guildChannel, discordGuildId, discordUserId, config);
            return;
        }

        var violationSummary = string.Join(", ", violations);
        var (effectiveAction, effectiveTimeoutMinutes, effectiveDeleteDays) = GetMostSevereModule(triggeredModules);

        logger.LogInformation(
            "Spam detected in guild {GuildId} from user {UserId}: {Violations} (DryRun={DryRun}, Action={Action})",
            discordGuildId, discordUserId, violationSummary, config.IsDryRun, effectiveAction);

        if (!config.IsDryRun) {
            try {
                await message.DeleteAsync();
            } catch (Exception ex) {
                logger.LogWarning(ex,
                    "Failed to delete spam message {MessageId} in guild {GuildId}",
                    message.Id, discordGuildId);
            }

            if (duplicateMessagesToDelete is not null) {
                foreach (var (channelId, msgId) in duplicateMessagesToDelete) {
                    if (msgId == message.Id)
                        continue;

                    try {
                        if (guildChannel.Guild.GetChannel(channelId) is IMessageChannel channel)
                            await channel.DeleteMessageAsync(msgId);
                    } catch (Exception ex) {
                        logger.LogWarning(ex,
                            "Failed to delete duplicate message {MessageId} in channel {ChannelId}",
                            msgId, channelId);
                    }
                }
            }

            await ApplyActionAsync(message, guildChannel.Guild, effectiveAction, effectiveTimeoutMinutes, effectiveDeleteDays);

            if (shouldLockdown) {
                logger.LogWarning(
                    "Auto-lockdown triggered for guild {GuildId} due to infraction from user {UserId}: {Violations}",
                    discordGuildId, discordUserId, violationSummary);

                _ = Task.Run(() => lockdownService.LockdownAsync(guildChannel.Guild.Id));
            }
        }

        if (config.LogChannelId.HasValue) {
            try {
                await SendLogMessageAsync(message, guildChannel.Guild, violations, effectiveAction, config.IsDryRun);
            } catch (Exception ex) {
                logger.LogWarning(ex,
                    "Failed to send log message for spam infraction in guild {GuildId}",
                    discordGuildId);
            }
        }

        if (config.IsMessageAuditEnabled) {
            _ = Task.Run(() => messageAuditService.RecordEventAsync(
                discordGuildId,
                message,
                violations,
                effectiveAction,
                config.IsDryRun,
                config.MessageAuditRetentionDays,
                duplicateMessagesToDelete,
                CancellationToken.None
            ));
        }
    }

    /// <summary>
    /// Runs the AI Sentinel module as the last step in the protection pipeline.
    /// Only executes if no other protection modules were triggered.
    /// Respects the module's own enable/disable and dry-run settings.
    /// In training mode, logs all analyses but only logs to the audit channel the ones that would have resulted in an action.
    /// </summary>
    private async Task RunAiSentinelAsync(
        SocketUserMessage message,
        SocketGuildChannel guildChannel,
        long discordGuildId,
        long discordUserId,
        SpamConfigurationDto spamConfig
    ) {
        AiSentinelConfigurationDto? aiConfig;

        using (var scope = scopeFactory.CreateScope()) {
            var aiCache = scope.ServiceProvider.GetRequiredService<AiSentinelCache>();
            aiConfig = await aiCache.GetByDiscordGuildIdAsync(discordGuildId);
        }

        if (aiConfig is null || !aiConfig.IsEnabled)
            return;

        if (string.IsNullOrWhiteSpace(aiConfig.Model))
            return;

        if (aiConfig.Provider != AiSentinelProvider.Ollama && string.IsNullOrWhiteSpace(aiConfig.ApiKey))
            return;

        if (aiConfig.Provider == AiSentinelProvider.Ollama && string.IsNullOrWhiteSpace(aiConfig.OllamaUrl))
            return;

        var result = await aiSentinelService.AnalyzeMessageAsync(message, aiConfig);
        if (result is null)
            return;

        var isMalicious = result.Classification != AiSentinelClassification.Clean;
        var wouldAction = isMalicious;

        var effectiveDryRun = aiConfig.IsTrainingMode || aiConfig.IsDryRun || spamConfig.IsDryRun;

        logger.LogInformation(
            "AI Sentinel analyzed message {MessageId} in guild {GuildId}: {Classification} (Reasoning={Reasoning}, WouldAction={WouldAction}, DryRun={DryRun}, TrainingMode={TrainingMode})",
            message.Id, discordGuildId, result.Classification, result.Reasoning, wouldAction, effectiveDryRun, aiConfig.IsTrainingMode);

        if (wouldAction && !effectiveDryRun) {
            try {
                await message.DeleteAsync();
            } catch (Exception ex) {
                logger.LogWarning(ex,
                    "Failed to delete AI-flagged message {MessageId} in guild {GuildId}",
                    message.Id, discordGuildId);
            }

            await ApplyActionAsync(message, guildChannel.Guild, aiConfig.Action, aiConfig.TimeoutMinutes, aiConfig.MessageDeleteDays);

            if (aiConfig.AutoLockdown && !spamConfig.IsLockedDown) {
                logger.LogWarning(
                    "Auto-lockdown triggered for guild {GuildId} due to AI Sentinel infraction from user {UserId}: {Classification}",
                    discordGuildId, discordUserId, result.Classification);

                _ = Task.Run(() => lockdownService.LockdownAsync(guildChannel.Guild.Id));
            }
        }

        if (wouldAction && spamConfig.LogChannelId.HasValue) {
            try {
                await SendAiLogMessageAsync(message, guildChannel.Guild, result, aiConfig, effectiveDryRun);
            } catch (Exception ex) {
                logger.LogWarning(ex,
                    "Failed to send AI sentinel log message in guild {GuildId}",
                    discordGuildId);
            }
        }

        if (wouldAction && spamConfig.IsMessageAuditEnabled) {
            _ = Task.Run(() => messageAuditService.RecordEventAsync(
                discordGuildId,
                message,
                [$"ai_sentinel_{result.Classification.ToString().ToLowerInvariant()}"],
                aiConfig.Action,
                effectiveDryRun,
                spamConfig.MessageAuditRetentionDays,
                null,
                CancellationToken.None
            ));
        }

        _ = Task.Run(() => aiSentinelService.LogAnalysisAsync(
            discordGuildId,
            message,
            aiConfig,
            result,
            wouldAction,
            effectiveDryRun,
            spamConfig.MessageAuditRetentionDays,
            CancellationToken.None
        ));
    }

    /// <summary>
    /// Sends an AI Sentinel infraction log embed to the configured log channel.
    /// </summary>
    private async Task SendAiLogMessageAsync(
        SocketUserMessage message,
        SocketGuild guild,
        AiSentinelAnalysisResult result,
        AiSentinelConfigurationDto aiConfig,
        bool isDryRun
    ) {
        using var scope = scopeFactory.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<SpamConfigurationCache>();
        var config = await cache.GetByDiscordGuildIdAsync((long)guild.Id);

        if (config?.LogChannelId is null)
            return;

        var channel = guild.GetTextChannel((ulong)config.LogChannelId.Value);
        if (channel is null)
            return;

        var actionLabel = isDryRun
            ? $"[DRY RUN] Would have applied: **{aiConfig.Action}**"
            : $"**{aiConfig.Action}**";

        var embed = new EmbedBuilder()
            .WithColor(isDryRun ? Color.Gold : Color.Red)
            .WithTitle(isDryRun ? "⚠️ Dry Run — AI Sentinel Detection" : "🤖 AI Sentinel Action Taken")
            .AddField("User", $"{message.Author.Mention} (`{message.Author.Id}`)", true)
            .AddField("Channel", $"<#{message.Channel.Id}>", true)
            .AddField("Classification", result.Classification.ToString())
            .AddField("Reasoning", result.Reasoning.Length > 1024 ? result.Reasoning[..1021] + "..." : result.Reasoning)
            .AddField("Model", $"{aiConfig.Provider} / {aiConfig.Model}", true)
            .AddField("Action", actionLabel, true)
            .WithTimestamp(message.Timestamp)
            .Build();

        try {
            await channel.SendMessageAsync(embed: embed);
        } catch (Exception ex) {
            logger.LogWarning(ex,
                "Failed to send AI log message to channel {ChannelId} in guild {GuildId}",
                config.LogChannelId.Value, guild.Id);
        }
    }

    /// <summary>
    /// Writes a debug log entry to a file for the specified test guild.
    /// Logs all messages regardless of spam detection outcome.
    /// </summary>
    private void LogDebugMessage(SocketUserMessage message, SocketGuildChannel guildChannel) {
        try {
            var logDir = Path.Combine(AppContext.BaseDirectory, "debug-logs");
            Directory.CreateDirectory(logDir);

            var logFile = Path.Combine(logDir, $"guild-{DebugLogGuildId}.log");

            var sb = new StringBuilder();
            sb.AppendLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] MsgId={message.Id} UserId={message.Author.Id} Username={message.Author.Username} Channel=#{guildChannel.Name} ChannelId={message.Channel.Id} IsBot={message.Author.IsBot} IsWebhook={message.Author.IsWebhook}");
            sb.AppendLine($"  Content: {message.Content}");

            if (message.Attachments.Count > 0) {
                sb.AppendLine("  Attachments:");
                foreach (var att in message.Attachments)
                    sb.AppendLine($"    - Id={att.Id} Filename={att.Filename} Size={att.Size} Url={att.Url} Width={att.Width} Height={att.Height}");
            }

            if (message.Embeds.Count > 0) {
                sb.AppendLine("  Embeds:");
                foreach (var embed in message.Embeds) {
                    sb.AppendLine($"    - Title={embed.Title} Description={embed.Description} Url={embed.Url} Type={embed.Type}");
                    if (embed.Image is { } img)
                        sb.AppendLine($"      Image: {img.Url}");
                    if (embed.Thumbnail is { } thumb)
                        sb.AppendLine($"      Thumbnail: {thumb.Url}");
                }
            }

            File.AppendAllText(logFile, sb.ToString());
        } catch (Exception ex) {
            logger.LogWarning(ex, "Failed to write debug log for guild {GuildId}", DebugLogGuildId);
        }
    }

    /// <summary>
    /// Returns the most severe module tuple from the list of triggered modules.
    /// Severity order: DeleteOnly &lt; Timeout &lt; Kick &lt; Ban.
    /// </summary>
    private static (InfractionAction Action, int TimeoutMinutes, int DeleteDays) GetMostSevereModule(
        IReadOnlyList<(InfractionAction Action, int TimeoutMinutes, int DeleteDays)> modules) {
        (InfractionAction Action, int TimeoutMinutes, int DeleteDays) max = (InfractionAction.DeleteOnly, 10, 0);
        foreach (var m in modules) {
            if (m.Action > max.Action)
                max = m;
        }
        return max;
    }

    /// <summary>
    /// Applies the configured infraction action against the offending user.
    /// </summary>
    private async Task ApplyActionAsync(
        SocketUserMessage message,
        SocketGuild guild,
        InfractionAction action,
        int timeoutMinutes,
        int deleteMessageDays
    ) {
        var guildUser = guild.GetUser(message.Author.Id);
        if (guildUser is null)
            return;

        var pruneDays = Math.Clamp(deleteMessageDays, 0, 7);
        var timeout = TimeSpan.FromMinutes(Math.Max(1, timeoutMinutes));

        try {
            switch (action) {
                case InfractionAction.Timeout:
                    await guildUser.SetTimeOutAsync(timeout);
                    break;
                case InfractionAction.Kick:
                    await guildUser.KickAsync("Spam violation detected by Blackwall.");
                    break;
                case InfractionAction.Ban:
                    await guild.AddBanAsync(guildUser, pruneDays, "Spam violation detected by Blackwall.");
                    break;
                case InfractionAction.SoftBan:
                    await guild.AddBanAsync(guildUser, pruneDays, "Spam violation detected by Blackwall.");
                    await guild.RemoveBanAsync(guildUser);
                    break;
                case InfractionAction.DeleteOnly:
                default:
                    break;
            }
        } catch (Exception ex) {
            logger.LogWarning(ex,
                "Failed to apply action {Action} against user {UserId} in guild {GuildId}",
                action, message.Author.Id, guild.Id);
        }
    }

    /// <summary>
    /// Builds a preview of the message content for embed fields.
    /// Falls back to a placeholder when the message has no text content,
    /// and includes attachment filenames when present.
    /// </summary>
    private static string BuildMessagePreview(SocketUserMessage message) {
        const int maxLen = 1024;

        var content = message.Content;
        if (string.IsNullOrWhiteSpace(content)) {
            content = message.Attachments.Count > 0
                ? $"*(no text content — {message.Attachments.Count} attachment(s))*"
                : "*(no text content)*";
        }

        if (content.Length > maxLen)
            content = content[..(maxLen - 3)] + "...";

        return content;
    }

    /// <summary>
    /// Sends an infraction log embed to the configured log channel.
    /// </summary>
    private async Task SendLogMessageAsync(
        SocketUserMessage message,
        SocketGuild guild,
        IReadOnlyList<string> violations,
        InfractionAction action,
        bool isDryRun
    ) {
        using var scope = scopeFactory.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<SpamConfigurationCache>();
        var config = await cache.GetByDiscordGuildIdAsync((long)guild.Id);

        if (config?.LogChannelId is null)
            return;

        var channel = guild.GetTextChannel((ulong)config.LogChannelId.Value);
        if (channel is null)
            return;

        var actionLabel = isDryRun
            ? $"[DRY RUN] Would have applied: **{action}**"
            : $"**{action}**";

        var embed = new EmbedBuilder()
            .WithColor(isDryRun ? Color.Gold : Color.Red)
            .WithTitle(isDryRun ? "⚠️ Dry Run — Infraction Detected" : "🛡️ Infraction Action Taken")
            .AddField("User", $"{message.Author.Mention} (`{message.Author.Id}`)", true)
            .AddField("Channel", $"<#{message.Channel.Id}>", true)
            .AddField("Triggers", string.Join(", ", violations))
            .AddField("Message", BuildMessagePreview(message))
            .AddField("Action", actionLabel)
            .WithTimestamp(message.Timestamp)
            .Build();

        try {
            await channel.SendMessageAsync(embed: embed);
        } catch (Exception ex) {
            logger.LogWarning(ex,
                "Failed to send log message to channel {ChannelId} in guild {GuildId}",
                config.LogChannelId.Value, guild.Id);
        }
    }
}
