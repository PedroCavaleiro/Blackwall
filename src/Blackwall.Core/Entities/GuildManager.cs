// ReSharper disable NullableWarningSuppressionIsUsed
namespace Blackwall.Core.Entities;

public class GuildManager: EntityBase {
    public long GuildInstanceId { get; set; }
    public GuildInstance GuildInstance { get; set; } = null!;

    public long UserId { get; set; }
    public AppUser User { get; set; } = null!;

    public long DiscordRoleId { get; set; }
    public bool IsAdmin { get; set; }
}