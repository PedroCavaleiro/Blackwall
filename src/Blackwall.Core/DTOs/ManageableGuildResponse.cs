// ReSharper disable NotAccessedPositionalProperty.Global
namespace Blackwall.Core.DTOs;

public sealed record ManageableGuildResponse(
    long DiscordGuildId,
    string Name,
    string? Icon,
    bool Owner,
    bool CanManage,
    bool BotInstalled,
    bool Claimed,
    bool CanOpen
);