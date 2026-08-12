// ReSharper disable NullableWarningSuppressionIsUsed
// ReSharper disable PropertyCanBeMadeInitOnly.Global
namespace Blackwall.Core.Entities;

public sealed class TwitchChannelModuleInstallation : EntityBase {
    public long TwitchChannelInstanceId { get; set; }
    public TwitchChannelInstance TwitchChannelInstance { get; set; } = null!;

    public string ModuleName { get; set; } = null!;
    public string ModuleVersion { get; set; } = null!;
    public string ModuleAuthor { get; set; } = null!;
    public string EntryPoint { get; set; } = null!;
    public string GitUrl { get; set; } = "";
    public bool CanPerformActions { get; set; }
    public bool IsEnabled { get; set; }
    public string? DisabledReason { get; set; }
    public string SettingsJson { get; set; } = "{}";
    public string ManifestJson { get; set; } = "{}";
    public DateTime UpdatedAtUtc { get; set; }
}
