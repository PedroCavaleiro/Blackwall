// ReSharper disable NullableWarningSuppressionIsUsed
namespace Blackwall.Core.Entities;

public sealed class GuildBlacklist: EntityBase {
    public long SpamConfigurationId { get; set; }
    public SpamConfiguration SpamConfiguration { get; set; } = null!;

    public string Url { get; set; } = string.Empty;
}
