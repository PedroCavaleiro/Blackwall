// ReSharper disable NotAccessedPositionalProperty.Global
namespace Blackwall.Core.DTOs;

public sealed record EmbedFieldDto(
    string Name,
    string Value,
    bool Inline
);

public sealed record EmbedDataDto(
    string? Title,
    string? Description,
    string? Url,
    int? Color,
    string? AuthorName,
    string? AuthorIconUrl,
    string? FooterText,
    string? FooterIconUrl,
    string? ThumbnailUrl,
    string? ImageUrl,
    DateTime? Timestamp,
    List<EmbedFieldDto> Fields
);
