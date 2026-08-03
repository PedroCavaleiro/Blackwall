namespace Blackwall.Modules.Abstractions;

public interface IBlackwallModule {
    string Name { get; }
    string Version { get; }

    Task InitializeAsync(ModuleSettings settings, CancellationToken ct);

    Task<ModuleVerdict?> EvaluateAsync(ModuleMessageContext context, CancellationToken ct);

    Task UpdateSettingsAsync(ModuleSettings settings, CancellationToken ct);
}
