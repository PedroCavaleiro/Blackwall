using Blackwall.Core.Entities;
using Blackwall.Modules.Abstractions;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
// ReSharper disable NullableWarningSuppressionIsUsed
// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable MemberCanBePrivate.Global

namespace Blackwall.Modules.Runtime;

public sealed record ModuleInstallationInfo(
    string ModuleName,
    string ModuleVersion,
    string SettingsJson,
    string EntryPoint
);

internal sealed class LoadedModule : IDisposable {
    public IBlackwallModule Instance { get; init; } = null!;
    public ModuleLoadContext Context { get; init; } = null!;
    public string ModuleKey { get; init; } = null!;

    public void Dispose() {
        try {
            Context.Unload();
        } catch {
            // ignored
        }
    }
}

internal sealed class ModuleLoadContext(string modulePath) : AssemblyLoadContext(isCollectible: true) {
    private readonly AssemblyDependencyResolver _resolver = new(modulePath);

    protected override Assembly? Load(AssemblyName assemblyName) {
        if (assemblyName.Name?.StartsWith("Blackwall", StringComparison.OrdinalIgnoreCase) == true)
            return null;

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is not null ? LoadFromAssemblyPath(path) : null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName) {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is not null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
    }
}

public sealed class ModuleRuntimeService(
    ILogger<ModuleRuntimeService> logger
) {
    private static readonly string ModulesBasePath = Path.Combine(AppContext.BaseDirectory, "modules");
    private static readonly TimeSpan EvaluationTimeout = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<string, LoadedModule> _loadedModules = new();

    public async Task<IBlackwallModule?> GetOrLoadModuleAsync(
        ModuleInstallationInfo info,
        CancellationToken cancellationToken
    ) {
        var key = $"{info.ModuleName}:{info.ModuleVersion}";

        if (_loadedModules.TryGetValue(key, out var existing))
            return existing.Instance;

        var dllPath = Path.Combine(ModulesBasePath, info.ModuleName, info.ModuleVersion, info.EntryPoint);

        if (!File.Exists(dllPath)) {
            logger.LogWarning(
                "Module DLL not found at {Path} for module {ModuleName}",
                dllPath, info.ModuleName);
            return null;
        }

        try {
            var loadContext = new ModuleLoadContext(dllPath);
            var assembly = loadContext.LoadFromAssemblyPath(dllPath);

            var moduleType = assembly.GetTypes()
                .FirstOrDefault(t => typeof(IBlackwallModule).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false });

            if (moduleType is null) {
                logger.LogError(
                    "No IBlackwallModule implementation found in assembly {Assembly} for module {ModuleName}",
                    assembly.FullName, info.ModuleName);
                loadContext.Unload();
                return null;
            }

            var instance = (IBlackwallModule)Activator.CreateInstance(moduleType)!;

            var settings = ParseSettings(info.SettingsJson);
            await instance.InitializeAsync(settings, cancellationToken);

            var loaded = new LoadedModule {
                Instance = instance,
                Context = loadContext,
                ModuleKey = key
            };

            _loadedModules[key] = loaded;
            return loaded.Instance;
        } catch (Exception ex) {
            logger.LogError(ex,
                "Failed to load module {ModuleName} from {Path}",
                info.ModuleName, dllPath);
            return null;
        }
    }

    public async Task<ModuleVerdict?> EvaluateAsync(
        IBlackwallModule moduleInstance,
        ModuleMessageContext context,
        CancellationToken cancellationToken
    ) {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(EvaluationTimeout);

        return await moduleInstance.EvaluateAsync(context, cts.Token);
    }

    public void UnloadModule(string moduleName, string version) {
        var key = $"{moduleName}:{version}";
        if (_loadedModules.TryRemove(key, out var loaded))
            loaded.Dispose();
    }

    public async Task ReloadModuleSettingsAsync(
        string moduleName,
        string version,
        string settingsJson,
        CancellationToken cancellationToken = default
    ) {
        var key = $"{moduleName}:{version}";
        if (!_loadedModules.TryGetValue(key, out var loaded))
            return;

        try {
            var settings = ParseSettings(settingsJson);
            await loaded.Instance.UpdateSettingsAsync(settings, cancellationToken);
        } catch (Exception ex) {
            logger.LogError(ex,
                "Failed to update settings for module {ModuleName}",
                moduleName);
        }
    }

    public void UnloadAllModules() {
        foreach (var kvp in _loadedModules) {
            try {
                kvp.Value.Dispose();
            } catch {
                // ignored
            }
        }
        _loadedModules.Clear();
    }

    public static InfractionAction MapAction(ModuleAction action) => action switch {
        ModuleAction.DeleteOnly => InfractionAction.DeleteOnly,
        ModuleAction.Timeout => InfractionAction.Timeout,
        ModuleAction.Kick => InfractionAction.Kick,
        ModuleAction.Ban => InfractionAction.Ban,
        ModuleAction.SoftBan => InfractionAction.SoftBan,
        _ => InfractionAction.DeleteOnly
    };

    public static ModuleSettings ParseSettings(string settingsJson) {
        var dict = JsonSerializer.Deserialize<Dictionary<string, string?>>(settingsJson, JsonOptions) ?? [];
        return new ModuleSettings(dict);
    }
}
