using Microsoft.Extensions.Configuration;

namespace Blackwall.Core.Configuration;

public sealed class TwitchSyncOptions {
    public const string SectionName = "TWITCH_SYNC";

    [ConfigurationKeyName("INTERVAL_MINUTES")]
    public int IntervalMinutes { get; set; } = 15;
    [ConfigurationKeyName("ENABLED")]
    public bool Enabled { get; set; } = true;
}
