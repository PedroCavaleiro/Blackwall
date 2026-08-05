using System.Text.Json;
using Blackwall.Core.DTOs;
using Blackwall.Core.Entities;
using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
// ReSharper disable UnusedParameter.Local

namespace Blackwall.DiscordBot.Services;

public sealed class MessageAuditService(
    IServiceScopeFactory scopeFactory,
    DiscordSocketClient discordClient,
    ILogger<MessageAuditService> logger
) {
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNamingPolicy = null
    };

    /// <summary>
    /// Records a protection event and all associated messages to the audit store.
    /// The primary message is always recorded; duplicate messages are also included if provided.
    /// </summary>
    public async Task RecordEventAsync(
        long discordGuildId,
        SocketUserMessage primaryMessage,
        IReadOnlyList<string> violations,
        InfractionAction action,
        bool isDryRun,
        int retentionDays,
        List<(ulong ChannelId, ulong MessageId)>? duplicateMessages,
        CancellationToken cancellationToken = default
    ) {
        try {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.BlackwallDbContext>();

            var guildInstance = await dbContext.GuildInstances
                .FirstOrDefaultAsync(g => g.DiscordGuildId == discordGuildId, cancellationToken);

            if (guildInstance is null)
                return;

            var channelName = (primaryMessage.Channel as SocketGuildChannel)?.Name ?? "unknown";
            var author = primaryMessage.Author;
            var expiresAt = DateTime.UtcNow.AddDays(Math.Clamp(retentionDays, 7, 90));

            var auditEvent = new MessageAuditEvent {
                GuildInstanceId = guildInstance.Id,
                DiscordUserId = (long)author.Id,
                Username = $"{author.Username}#{author.Discriminator}",
                AvatarHash = author.AvatarId,
                DiscordChannelId = (long)primaryMessage.Channel.Id,
                ChannelName = channelName,
                Violations = string.Join(", ", violations),
                Action = action,
                IsDryRun = isDryRun
            };

            dbContext.MessageAuditEvents.Add(auditEvent);
            await dbContext.SaveChangesAsync(cancellationToken);

            var records = new List<MessageAuditRecord> {
                BuildRecord(auditEvent.Id, primaryMessage, author, channelName, expiresAt)
            };

            if (duplicateMessages is not null) {
                foreach (var (channelId, msgId) in duplicateMessages) {
                    if (msgId == primaryMessage.Id)
                        continue;

                    var dupRecord = await TryBuildRecordFromDiscordAsync(
                        auditEvent.Id, discordGuildId, channelId, msgId, expiresAt, cancellationToken);
                    if (dupRecord is not null)
                        records.Add(dupRecord);
                }
            }

            dbContext.MessageAuditRecords.AddRange(records);
            await dbContext.SaveChangesAsync(cancellationToken);
        } catch (Exception ex) {
            logger.LogWarning(ex, "Failed to record message audit event for guild {GuildId}", discordGuildId);
        }
    }

    /// <summary>
    /// Purges all audit records and events that have expired past their retention period.
    /// </summary>
    public async Task PurgeExpiredAsync(CancellationToken cancellationToken = default) {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.BlackwallDbContext>();

        var now = DateTime.UtcNow;

        var expiredRecordIds = await dbContext.MessageAuditRecords
            .Where(r => r.ExpiresAtUtc < now)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        if (expiredRecordIds.Count > 0) {
            await dbContext.MessageAuditRecords
                .Where(r => expiredRecordIds.Contains(r.Id))
                .ExecuteDeleteAsync(cancellationToken);
            logger.LogInformation("Purged {Count} expired message audit records", expiredRecordIds.Count);
        }

        var orphanedEventIds = await dbContext.MessageAuditEvents
            .Where(e => !dbContext.MessageAuditRecords.Any(r => r.EventId == e.Id))
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);

        if (orphanedEventIds.Count > 0) {
            await dbContext.MessageAuditEvents
                .Where(e => orphanedEventIds.Contains(e.Id))
                .ExecuteDeleteAsync(cancellationToken);
            logger.LogInformation("Purged {Count} orphaned message audit events", orphanedEventIds.Count);
        }
    }

    private MessageAuditRecord BuildRecord(
        long eventId,
        SocketUserMessage message,
        IUser author,
        string channelName,
        DateTime expiresAt
    ) {
        var embeds = ExtractEmbeds(message);
        return new MessageAuditRecord {
            EventId = eventId,
            DiscordMessageId = (long)message.Id,
            DiscordUserId = (long)author.Id,
            Username = $"{author.Username}#{author.Discriminator}",
            AvatarHash = author.AvatarId,
            DiscordChannelId = (long)message.Channel.Id,
            ChannelName = channelName,
            Content = message.Content ?? "",
            EmbedsJson = JsonSerializer.Serialize(embeds, JsonOptions),
            MessageTimestampUtc = message.Timestamp.UtcDateTime,
            ExpiresAtUtc = expiresAt
        };
    }

    private async Task<MessageAuditRecord?> TryBuildRecordFromDiscordAsync(
        long eventId,
        long discordGuildId,
        ulong channelId,
        ulong messageId,
        DateTime expiresAt,
        CancellationToken cancellationToken
    ) {
        try {
            var guild = discordClient.GetGuild((ulong)discordGuildId);
            if (guild?.GetChannel(channelId) is not IMessageChannel channel)
                return null;

            var msg = await channel.GetMessageAsync(messageId);
            if (msg is null)
                return null;

            var channelName = guild.GetChannel(channelId)?.Name ?? "unknown";
            var embeds = ExtractEmbeds(msg);
            return new MessageAuditRecord {
                EventId = eventId,
                DiscordMessageId = (long)msg.Id,
                DiscordUserId = (long)msg.Author.Id,
                Username = $"{msg.Author.Username}#{msg.Author.Discriminator}",
                AvatarHash = msg.Author.AvatarId,
                DiscordChannelId = (long)channelId,
                ChannelName = channelName,
                Content = msg.Content ?? "",
                EmbedsJson = JsonSerializer.Serialize(embeds, JsonOptions),
                MessageTimestampUtc = msg.Timestamp.UtcDateTime,
                ExpiresAtUtc = expiresAt
            };
        } catch (Exception ex) {
            logger.LogWarning(ex, "Failed to fetch duplicate message {MessageId} in channel {ChannelId} for audit", messageId, channelId);
            return null;
        }
    }

    private static List<EmbedDataDto> ExtractEmbeds(IMessage message) {
        var result = new List<EmbedDataDto>();
        foreach (var embed in message.Embeds) {
            result.Add(new EmbedDataDto(
                Title: embed.Title,
                Description: embed.Description,
                Url: embed.Url,
                Color: embed.Color is { } c ? (int)c.RawValue : null,
                AuthorName: embed.Author?.Name,
                AuthorIconUrl: embed.Author?.IconUrl,
                FooterText: embed.Footer?.Text,
                FooterIconUrl: embed.Footer?.IconUrl,
                ThumbnailUrl: embed.Thumbnail?.Url,
                ImageUrl: embed.Image?.Url,
                Timestamp: embed.Timestamp?.UtcDateTime,
                Fields: embed.Fields.Select(f => new EmbedFieldDto(f.Name, f.Value, f.Inline)).ToList()
            ));
        }
        return result;
    }
}
