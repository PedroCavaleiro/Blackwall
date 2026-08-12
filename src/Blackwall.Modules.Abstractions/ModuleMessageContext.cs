// ReSharper disable NotAccessedPositionalProperty.Global
namespace Blackwall.Modules.Abstractions;

public sealed record ModuleAttachment(
    long Id,
    string Filename,
    int Size,
    string Url,
    int? Width,
    int? Height
);

public sealed record ModuleEmbed(
    string? Title,
    string? Description,
    string? Url,
    string? Type,
    string? ImageUrl,
    string? ThumbnailUrl
);

public sealed record ModuleMessageContext(
    ModulePlatform Platform,
    long CommunityId,
    long UserId,
    long ChannelId,
    string ChannelName,
    string Username,
    bool IsBot,
    string Content,
    IReadOnlyList<ModuleAttachment> Attachments,
    IReadOnlyList<ModuleEmbed> Embeds,
    DateTime MessageTimestampUtc
);
