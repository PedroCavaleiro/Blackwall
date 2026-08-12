using Blackwall.Core.DTOs;
using Blackwall.Core.Entities;
using Blackwall.Infrastructure.Cache.Twitch;
using Blackwall.Modules.Abstractions;
using Blackwall.Modules.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TwitchLib.Client.Events;
// ReSharper disable NotAccessedPositionalProperty.Global
// ReSharper disable NullableWarningSuppressionIsUsed
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Blackwall.Bot.Twitch.Services;

public sealed record TwitchModuleEvaluationResult(
    string ViolationType,
    string ModuleName,
    string? ReadableName,
    InfractionAction Action,
    int TimeoutMinutes,
    int DeleteDays,
    bool AutoLockdown,
    string? Reason
);

public sealed class TwitchModuleRunnerService(
    IServiceScopeFactory scopeFactory,
    ModuleRuntimeService runtime,
    ILogger<TwitchModuleRunnerService> logger
) {
    public async Task<IReadOnlyList<TwitchModuleEvaluationResult>> EvaluateAsync(
        long twitchUserId,
        OnMessageReceivedArgs e,
        bool isDryRun,
        CancellationToken cancellationToken
    ) {
        IReadOnlyList<TwitchChannelModuleInstallationDto> installations;

        using (var scope = scopeFactory.CreateScope()) {
            var cache = scope.ServiceProvider.GetRequiredService<TwitchModuleInstallationCache>();
            installations = await cache.GetByTwitchUserIdAsync(twitchUserId, cancellationToken);
        }

        if (installations.Count == 0)
            return [];

        var context = BuildMessageContext(twitchUserId, e);
        var results = new List<TwitchModuleEvaluationResult>();

        foreach (var installation in installations) {
            try {
                var moduleInstance = await runtime.GetOrLoadModuleAsync(
                    new ModuleInstallationInfo(
                        installation.ModuleName,
                        installation.ModuleVersion,
                        installation.SettingsJson,
                        installation.Manifest.EntryPoint
                    ),
                    cancellationToken
                );
                if (moduleInstance is null)
                    continue;

                var verdict = await runtime.EvaluateAsync(moduleInstance, context, cancellationToken);

                if (verdict is not null) {
                    results.Add(new TwitchModuleEvaluationResult(
                        verdict.ViolationType,
                        installation.ModuleName,
                        installation.ReadableName,
                        ModuleRuntimeService.MapAction(verdict.Action),
                        verdict.TimeoutMinutes,
                        verdict.DeleteDays,
                        verdict.AutoLockdown,
                        verdict.Reason
                    ));
                }
            } catch (OperationCanceledException) {
                logger.LogWarning(
                    "Module {ModuleName} timed out evaluating message {MessageId} in channel {TwitchUserId}",
                    installation.ModuleName, e.ChatMessage.Id, twitchUserId);
            } catch (Exception ex) {
                logger.LogError(ex,
                    "Module {ModuleName} threw an exception evaluating message {MessageId} in channel {TwitchUserId}",
                    installation.ModuleName, e.ChatMessage.Id, twitchUserId);
            }
        }

        return results;
    }

    public void UnloadModule(string moduleName, string version) {
        runtime.UnloadModule(moduleName, version);
    }

    public Task ReloadModuleSettingsAsync(
        string moduleName,
        string version,
        string settingsJson,
        CancellationToken cancellationToken = default
    ) => runtime.ReloadModuleSettingsAsync(moduleName, version, settingsJson, cancellationToken);

    public void UnloadAllModules() => runtime.UnloadAllModules();

    private static ModuleMessageContext BuildMessageContext(
        long twitchUserId,
        OnMessageReceivedArgs e
    ) {
        var cm = e.ChatMessage;

        return new ModuleMessageContext(
            ModulePlatform.Twitch,
            twitchUserId,
            long.Parse(cm.UserId),
            twitchUserId,
            cm.Channel,
            cm.Username,
            cm.IsBroadcaster,
            cm.Message,
            [],
            [],
            DateTime.UtcNow
        );
    }
}
