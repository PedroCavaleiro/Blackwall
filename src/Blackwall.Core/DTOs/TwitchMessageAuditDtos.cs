using Blackwall.Core.Entities;
// ReSharper disable NotAccessedPositionalProperty.Global

namespace Blackwall.Core.DTOs;

public sealed record TwitchMessageAuditEventSummaryDto(
    long Id,
    long TwitchUserId,
    string Username,
    string Violations,
    InfractionAction Action,
    bool IsDryRun,
    DateTime CreatedAtUtc,
    int MessageCount
);

public sealed record TwitchMessageAuditMessageDto(
    long Id,
    string MessageId,
    long TwitchUserId,
    string Username,
    string Content,
    DateTime MessageTimestampUtc
);

public sealed record TwitchMessageAuditEventDetailDto(
    long Id,
    long TwitchUserId,
    string Username,
    string Violations,
    InfractionAction Action,
    bool IsDryRun,
    DateTime CreatedAtUtc,
    List<TwitchMessageAuditMessageDto> Messages
);
