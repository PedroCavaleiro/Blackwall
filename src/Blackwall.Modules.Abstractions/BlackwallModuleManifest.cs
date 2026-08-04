namespace Blackwall.Modules.Abstractions;

public sealed class ModuleFieldOption {
    public string Text { get; set; } = null!;
    public string Value { get; set; } = null!;
}

public sealed class ModuleSettingsField {
    public string Key { get; set; } = null!;
    public string UiName { get; set; } = null!;
    public string? HelpText { get; set; }
    public ModuleInputType InputType { get; set; }
    public string DefaultValue { get; set; } = "";
    public List<ModuleFieldOption>? Options { get; set; }
}

public sealed class ModuleSettingsCard {
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public List<ModuleSettingsField> Fields { get; set; } = [];
}

public sealed class ModuleSettingsSchema {
    public List<ModuleSettingsCard> Cards { get; set; } = [];
}

public sealed class BlackwallModuleManifest {
    public string Name { get; set; } = null!;
    public string? ReadableName { get; set; }
    public string Version { get; set; } = null!;
    public string Author { get; set; } = null!;
    public string? Description { get; set; }
    public string EntryPoint { get; set; } = null!;
    public bool CanPerformActions { get; set; }
    public ModuleSettingsSchema? SettingsSchema { get; set; }
}
