using Blackwall.Modules.Abstractions;

namespace Blackwall.Core.DTOs;

public sealed record ModuleFieldOptionDto(
    string Text,
    string Value
);

public sealed record ModuleSettingsFieldDto(
    string Key,
    string UiName,
    string? HelpText,
    ModuleInputType InputType,
    string DefaultValue,
    IReadOnlyList<ModuleFieldOptionDto>? Options
);

public sealed record ModuleSettingsCardDto(
    string Title,
    string? Description,
    IReadOnlyList<ModuleSettingsFieldDto> Fields
);

public sealed record ModuleSettingsSchemaDto(
    IReadOnlyList<ModuleSettingsCardDto> Cards
);

public sealed record BlackwallModuleManifestDto(
    string Name,
    string? ReadableName,
    string Version,
    string Author,
    string? Description,
    string EntryPoint,
    bool CanPerformActions,
    IReadOnlyList<ModulePlatform> Platforms,
    ModuleSettingsSchemaDto? SettingsSchema
);

public sealed record GuildModuleInstallationDto(
    long Id,
    long DiscordGuildId,
    string ModuleName,
    string? ReadableName,
    string ModuleVersion,
    string ModuleAuthor,
    string? Description,
    string GitUrl,
    bool CanPerformActions,
    bool IsEnabled,
    string SettingsJson,
    BlackwallModuleManifestDto Manifest
);

public sealed record TwitchChannelModuleInstallationDto(
    long Id,
    long TwitchUserId,
    string ModuleName,
    string? ReadableName,
    string ModuleVersion,
    string ModuleAuthor,
    string? Description,
    string GitUrl,
    bool CanPerformActions,
    bool IsEnabled,
    string SettingsJson,
    BlackwallModuleManifestDto Manifest
);

public sealed record InstallModuleRequest(
    string GitUrl
);

public sealed record UpdateModuleSettingsRequest(
    string SettingsJson
);

public sealed record UpdateModuleEnabledRequest(
    bool IsEnabled
);
