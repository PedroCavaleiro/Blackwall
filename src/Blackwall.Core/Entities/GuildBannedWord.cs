// ReSharper disable NullableWarningSuppressionIsUsed
namespace Blackwall.Core.Entities;

public sealed class GuildBannedWord : EntityBase {
    public long SpamConfigurationId { get; set; }
    public SpamConfiguration SpamConfiguration { get; set; } = null!;

    public string Word { get; set; } = string.Empty;
}
