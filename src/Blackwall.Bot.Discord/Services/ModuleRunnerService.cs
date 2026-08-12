using Blackwall.Core.DTOs;
using Blackwall.Core.Entities;
using Blackwall.Modules.DetectionMatrix;
using Blackwall.Infrastructure.Cache.Discord;
using Blackwall.Modules.Abstractions;
using Blackwall.Modules.Runtime;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

public sealed class ModuleRunnerService(
    IServiceScopeFactory scopeFactory,
    ModuleRuntimeService runtime,
    ILogger<ModuleRunnerService> logger
) {
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
                    results.Add(new ModuleEvaluationResult(
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
                    "Module {ModuleName} timed out evaluating message {MessageId} in guild {GuildId}",
                    installation.ModuleName, message.Id, discordGuildId);
            } catch (Exception ex) {
                logger.LogError(ex,
                    "Module {ModuleName} threw an exception evaluating message {MessageId} in guild {GuildId}",
                    installation.ModuleName, message.Id, discordGuildId);
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
            ModulePlatform.Discord,
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
