namespace Blackwall.Core.Entities;

public sealed class AppUser: EntityBase {

    public long DiscordUserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? DisplayName { get; set; } = null;

    public ICollection<GuildInstance> OwnedGuilds { get; set; } = [];
    public ICollection<GuildManager> ManagedGuilds { get; set; } = [];

}