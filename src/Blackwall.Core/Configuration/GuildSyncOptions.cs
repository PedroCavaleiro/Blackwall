using Microsoft.Extensions.Configuration;

namespace Blackwall.Core.Configuration;

public sealed class GuildSyncOptions {
    public const string SectionName = "GUILD_SYNC";

    [ConfigurationKeyName("INTERVAL_MINUTES")]
    public int IntervalMinutes { get; set; } = 15;
    [ConfigurationKeyName("ENABLED")]
    public bool Enabled { get; set; } = true;
}