using Blackwall.Core.DTOs;
using Blackwall.Core.Entities;
using Blackwall.Modules.DetectionMatrix;
using Blackwall.Infrastructure.Cache.Discord;
using Blackwall.Modules.Abstractions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
// ReSharper disable NotAccessedPositionalProperty.Global
// ReSharper disable NullableWarningSuppressionIsUsed
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Blackwall.Bot.Discord.Services;

public sealed record ModuleEvaluationResult(
    string ViolationType,
    string ModuleName,
    string? ReadableName,
    InfractionAction Action,
    int TimeoutMinutes,
    int DeleteDays,
    bool AutoLockdown,
    string? Reason
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

public sealed class ModuleRunnerService(
    IServiceScopeFactory scopeFactory,
    ILogger<ModuleRunnerService> logger
) {
    private static readonly string ModulesBasePath = Path.Combine(AppContext.BaseDirectory, "modules");
    private static readonly TimeSpan EvaluationTimeout = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<string, LoadedModule> _loadedModules = new();

    public async Task<IReadOnlyList<ModuleEvaluationResult>> EvaluateAsync(
        long discordGuildId,
        SocketUserMessage message,
        SocketGuildChannel guildChannel,
        bool isDryRun,
        CancellationToken cancellationToken
    ) {
        IReadOnlyList<GuildModuleInstallationDto> installations;

        using (var scope = scopeFactory.CreateScope()) {
            var cache = scope.ServiceProvider.GetRequiredService<ModuleInstallationCache>();
            installations = await cache.GetByDiscordGuildIdAsync(discordGuildId, cancellationToken);
        }

        if (installations.Count == 0)
            return [];

        var context = BuildMessageContext(message, guildChannel, discordGuildId);
        var results = new List<ModuleEvaluationResult>();

        foreach (var installation in installations) {
            try {
                var loaded = await GetOrLoadModuleAsync(installation, cancellationToken);
                if (loaded is null)
                    continue;

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(EvaluationTimeout);

                var verdict = await loaded.Instance.EvaluateAsync(context, cts.Token);

                if (verdict is not null) {
                    results.Add(new ModuleEvaluationResult(
                        verdict.ViolationType,
                        installation.ModuleName,
                        installation.ReadableName,
                        MapAction(verdict.Action),
                        verdict.TimeoutMinutes,
                        verdict.DeleteDays,
                        verdict.AutoLockdown,
                        verdict.Reason
                    ));
                }
            } catch (OperationCanceledException) {
                logger.LogWarning(
                    "Module {ModuleName} timed out after {Timeout}s evaluating message {MessageId} in guild {GuildId}",
                    installation.ModuleName, EvaluationTimeout.TotalSeconds, message.Id, discordGuildId);
            } catch (Exception ex) {
                logger.LogError(ex,
                    "Module {ModuleName} threw an exception evaluating message {MessageId} in guild {GuildId}",
                    installation.ModuleName, message.Id, discordGuildId);
            }
        }

        return results;
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

    private async Task<LoadedModule?> GetOrLoadModuleAsync(
        GuildModuleInstallationDto installation,
        CancellationToken cancellationToken
    ) {
        var key = $"{installation.ModuleName}:{installation.ModuleVersion}";

        if (_loadedModules.TryGetValue(key, out var existing))
            return existing;

        var entryPoint = installation.Manifest.EntryPoint;
        var dllPath = Path.Combine(ModulesBasePath, installation.ModuleName, installation.ModuleVersion, entryPoint);

        if (!File.Exists(dllPath)) {
            logger.LogWarning(
                "Module DLL not found at {Path} for module {ModuleName}",
                dllPath, installation.ModuleName);
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
                    assembly.FullName, installation.ModuleName);
                loadContext.Unload();
                return null;
            }

            var instance = (IBlackwallModule)Activator.CreateInstance(moduleType)!;

            var settings = ParseSettings(installation.SettingsJson);
            await instance.InitializeAsync(settings, cancellationToken);

            var loaded = new LoadedModule {
                Instance = instance,
                Context = loadContext,
                ModuleKey = key
            };

            _loadedModules[key] = loaded;
            return loaded;
        } catch (Exception ex) {
            logger.LogError(ex,
                "Failed to load module {ModuleName} from {Path}",
                installation.ModuleName, dllPath);
            return null;
        }
    }

    private static ModuleSettings ParseSettings(string settingsJson) {
        var dict = JsonSerializer.Deserialize<Dictionary<string, string?>>(settingsJson, JsonOptions) ?? [];
        return new ModuleSettings(dict);
    }

    private static InfractionAction MapAction(ModuleAction action) => action switch {
        ModuleAction.DeleteOnly => InfractionAction.DeleteOnly,
        ModuleAction.Timeout => InfractionAction.Timeout,
        ModuleAction.Kick => InfractionAction.Kick,
        ModuleAction.Ban => InfractionAction.Ban,
        ModuleAction.SoftBan => InfractionAction.SoftBan,
        _ => InfractionAction.DeleteOnly
    };

    private static ModuleMessageContext BuildMessageContext(
        SocketUserMessage message,
        SocketGuildChannel guildChannel,
        long discordGuildId
    ) {
        var attachments = message.Attachments
            .Select(a => new ModuleAttachment(
                (long)a.Id,
                a.Filename,
                a.Size,
                a.Url,
                a.Width,
                a.Height
            ))
            .ToList();

        var embeds = message.Embeds
            .Select(e => new ModuleEmbed(
                e.Title,
                e.Description,
                e.Url,
                e.Type.ToString(),
                e.Image?.Url,
                e.Thumbnail?.Url
            ))
            .ToList();

        var fullContent = DetectionService.ExtractFullContent(message);

        return new ModuleMessageContext(
            discordGuildId,
            (long)message.Author.Id,
            (long)message.Channel.Id,
            guildChannel.Name,
            message.Author.Username,
            message.Author.IsBot,
            fullContent,
            attachments,
            embeds,
            message.Timestamp.UtcDateTime
        );
    }
}
