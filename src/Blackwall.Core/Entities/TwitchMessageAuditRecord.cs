// ReSharper disable PropertyCanBeMadeInitOnly.Global
// ReSharper disable NullableWarningSuppressionIsUsed
namespace Blackwall.Core.Entities;

public class TwitchMessageAuditRecord : EntityBase {
    public long EventId { get; set; }
    public TwitchMessageAuditEvent Event { get; set; } = null!;

    public string DiscordMessageId { get; set; } = "";
    public long TwitchUserId { get; set; }
    public string Username { get; set; } = "";

    public string Content { get; set; } = "";

    public DateTime MessageTimestampUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}
