using Blackwall.Core.Entities;

namespace Blackwall.Web.Components.Shared;

public sealed class TwitchChannelConfigForm {
    public bool IsEnabled { get; set; } = true;
    public bool IsDryRun { get; set; }
    public bool AutoAddManagers { get; set; } = true;
    public string CommandTrigger { get; set; } = "!";

    public int MaxMessagesPerWindow { get; set; }
    public int RateLimitWindowSeconds { get; set; }
    public int DuplicateMessageThreshold { get; set; }
    public int DuplicateWindowSeconds { get; set; } = 5;
    public int MentionLimit { get; set; }

    public InfractionAction RateLimitAction { get; set; } = InfractionAction.Timeout;
    public int RateLimitTimeoutMinutes { get; set; } = 10;

    public InfractionAction DuplicateAction { get; set; } = InfractionAction.Timeout;
    public int DuplicateTimeoutMinutes { get; set; } = 10;

    public InfractionAction MentionLimitAction { get; set; } = InfractionAction.Timeout;
    public int MentionLimitTimeoutMinutes { get; set; } = 10;
}
