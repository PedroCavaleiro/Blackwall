// ReSharper disable NullableWarningSuppressionIsUsed
namespace Blackwall.Core.Entities;

public sealed class GuildAllowedBot : EntityBase {
    public long SpamConfigurationId { get; set; }
    public SpamConfiguration SpamConfiguration { get; set; } = null!;

    public long DiscordBotId { get; set; }
    public string BotUsername { get; set; } = string.Empty;
}
