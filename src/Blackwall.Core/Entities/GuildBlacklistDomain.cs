// ReSharper disable NullableWarningSuppressionIsUsed
namespace Blackwall.Core.Entities;

public sealed class GuildBlacklistDomain: EntityBase {
    public long SpamConfigurationId { get; set; }
    public SpamConfiguration SpamConfiguration { get; set; } = null!;

    public string Domain { get; set; } = string.Empty;
}
