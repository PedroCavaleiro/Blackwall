// ReSharper disable PropertyCanBeMadeInitOnly.Global
// ReSharper disable NullableWarningSuppressionIsUsed
namespace Blackwall.Core.Entities;

public class AiSentinelLog : EntityBase {
    public long AiSentinelConfigurationId { get; set; }
    public AiSentinelConfiguration AiSentinelConfiguration { get; set; } = null!;

    public long DiscordMessageId { get; set; }
    public long DiscordUserId { get; set; }
    public string Username { get; set; } = "";
    public string? AvatarHash { get; set; }

    public long DiscordChannelId { get; set; }
    public string ChannelName { get; set; } = "";

    public string Content { get; set; } = "";
    public string EmbedsJson { get; set; } = "[]";

    public AiSentinelClassification Classification { get; set; }
    public string Reasoning { get; set; } = "";

    public AiSentinelProvider Provider { get; set; }
    public string Model { get; set; } = "";

    public bool IsDryRun { get; set; }
    public bool WouldAction { get; set; }
    public AiSentinelTrainingFeedback TrainingFeedback { get; set; } = AiSentinelTrainingFeedback.None;

    public DateTime MessageTimestampUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}
