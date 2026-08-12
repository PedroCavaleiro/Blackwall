using Blackwall.Core.Entities;
using Blackwall.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TwitchLib.Client.Events;

namespace Blackwall.Bot.Twitch;

public sealed class TwitchMessageAuditService(
    IServiceScopeFactory scopeFactory,
    ILogger<TwitchMessageAuditService> logger
) {
    /// <summary>
    /// Records a protection event and the message that triggered it to the audit store.
    /// </summary>
    public async Task RecordEventAsync(
        long broadcasterId,
        OnMessageReceivedArgs messageArgs,
        IReadOnlyList<string> violations,
        InfractionAction action,
        bool isDryRun,
        int retentionDays,
        CancellationToken cancellationToken = default
    ) {
        try {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<BlackwallDbContext>();

            var channelInstance = await dbContext.TwitchChannelInstances
                .FirstOrDefaultAsync(c => c.TwitchUserId == broadcasterId, cancellationToken);

            if (channelInstance is null)
                return;

            var expiresAt = DateTime.UtcNow.AddDays(Math.Clamp(retentionDays, 7, 90));
            var chatMsg = messageArgs.ChatMessage;

            var auditEvent = new TwitchMessageAuditEvent {
                TwitchChannelInstanceId = channelInstance.Id,
                TwitchUserId = long.Parse(chatMsg.UserId),
                Username = chatMsg.Username,
                Violations = string.Join(", ", violations),
                Action = action,
                IsDryRun = isDryRun
            };

            dbContext.TwitchMessageAuditEvents.Add(auditEvent);
            await dbContext.SaveChangesAsync(cancellationToken);

            var record = new TwitchMessageAuditRecord {
                EventId = auditEvent.Id,
                DiscordMessageId = chatMsg.Id,
                TwitchUserId = long.Parse(chatMsg.UserId),
                Username = chatMsg.Username,
                Content = chatMsg.Message,
                MessageTimestampUtc = DateTime.UtcNow,
                ExpiresAtUtc = expiresAt
            };

            dbContext.TwitchMessageAuditRecords.Add(record);
            await dbContext.SaveChangesAsync(cancellationToken);
        } catch (Exception ex) {
            logger.LogWarning(ex, "Failed to record Twitch message audit event for channel {BroadcasterId}", broadcasterId);
        }
    }

    /// <summary>
    /// Purges all audit records and events that have expired past their retention period.
    /// </summary>
    public async Task PurgeExpiredAsync(CancellationToken cancellationToken = default) {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BlackwallDbContext>();

        var now = DateTime.UtcNow;

        var expiredRecordIds = await dbContext.TwitchMessageAuditRecords
            .Where(r => r.ExpiresAtUtc < now)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        if (expiredRecordIds.Count > 0) {
            await dbContext.TwitchMessageAuditRecords
                .Where(r => expiredRecordIds.Contains(r.Id))
                .ExecuteDeleteAsync(cancellationToken);
            logger.LogInformation("Purged {Count} expired Twitch message audit records", expiredRecordIds.Count);
        }

        var orphanedEventIds = await dbContext.TwitchMessageAuditEvents
            .Where(e => !dbContext.TwitchMessageAuditRecords.Any(r => r.EventId == e.Id))
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);

        if (orphanedEventIds.Count > 0) {
            await dbContext.TwitchMessageAuditEvents
                .Where(e => orphanedEventIds.Contains(e.Id))
                .ExecuteDeleteAsync(cancellationToken);
            logger.LogInformation("Purged {Count} orphaned Twitch message audit events", orphanedEventIds.Count);
        }
    }
}
