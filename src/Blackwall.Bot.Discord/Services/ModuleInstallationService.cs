using Blackwall.Core.Entities;
using Blackwall.Infrastructure.Cache.Discord;
using Blackwall.Infrastructure.Persistence;
using Blackwall.Modules.Abstractions;
using Blackwall.Modules.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Blackwall.Bot.Discord.Services;

public sealed class ModuleInstallationService(
    BlackwallDbContext dbContext,
    ModuleInstallationCache cache,
    ModuleRunnerService moduleRunnerService,
    ILogger<ModuleInstallationService> logger
) {
    private static readonly JsonSerializerOptions JsonOptions = ModuleBuildHelper.JsonOptions;

    public async Task<GuildModuleInstallation> InstallAsync(
        long discordGuildId,
        string gitUrl,
        CancellationToken cancellationToken = default
    ) {
        if (!Uri.TryCreate(gitUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Git URL must be a valid HTTPS URL.");

        var guildInstance = await dbContext.GuildInstances
            .FirstOrDefaultAsync(x => x.DiscordGuildId == discordGuildId && x.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("Guild not found or not active.");

        var tempDir = ModuleBuildHelper.CreateTempDir();

        try {
            var (manifestJson, manifest) = await ModuleBuildHelper.CloneAndReadManifestAsync(gitUrl, tempDir, cancellationToken);
            ModuleBuildHelper.ValidateManifest(manifest, ModulePlatform.Discord);

            var existing = await dbContext.GuildModuleInstallations
                .FirstOrDefaultAsync(x => x.GuildInstanceId == guildInstance.Id && x.ModuleName == manifest.Name, cancellationToken);

            if (existing is not null)
                throw new InvalidOperationException($"Module '{manifest.Name}' is already installed for this guild.");

            await ModuleBuildHelper.BuildAndCopyModuleAsync(manifest, tempDir, cancellationToken);

            var defaultSettings = ModuleBuildHelper.BuildDefaultSettings(manifest);

            var installation = new GuildModuleInstallation {
                GuildInstanceId = guildInstance.Id,
                ModuleName = manifest.Name,
                ModuleVersion = manifest.Version,
                ModuleAuthor = manifest.Author,
                EntryPoint = manifest.EntryPoint,
                GitUrl = gitUrl,
                CanPerformActions = manifest.CanPerformActions,
                IsEnabled = true,
                SettingsJson = JsonSerializer.Serialize(defaultSettings, JsonOptions),
                ManifestJson = manifestJson,
                UpdatedAtUtc = DateTime.UtcNow
            };

            dbContext.GuildModuleInstallations.Add(installation);
            await dbContext.SaveChangesAsync(cancellationToken);

            await cache.InvalidateAsync(discordGuildId);

            logger.LogInformation(
                "Module {ModuleName} v{Version} installed for guild {GuildId}",
                manifest.Name, manifest.Version, discordGuildId);

            return installation;
        } finally {
            ModuleBuildHelper.CleanupTempDir(tempDir);
        }
    }

    public async Task UninstallAsync(
        long discordGuildId,
        string moduleName,
        CancellationToken cancellationToken = default
    ) {
        var installation = await dbContext.GuildModuleInstallations
            .FirstOrDefaultAsync(x =>
                x.GuildInstance.DiscordGuildId == discordGuildId &&
                x.GuildInstance.IsActive &&
                x.ModuleName == moduleName, cancellationToken)
            ?? throw new InvalidOperationException($"Module '{moduleName}' is not installed for this guild.");

        dbContext.GuildModuleInstallations.Remove(installation);
        await dbContext.SaveChangesAsync(cancellationToken);

        await cache.InvalidateAsync(discordGuildId);

        logger.LogInformation(
            "Module {ModuleName} uninstalled from guild {GuildId}",
            moduleName, discordGuildId);
    }

    public async Task<GuildModuleInstallation> UpdateAsync(
        long discordGuildId,
        string moduleName,
        CancellationToken cancellationToken = default
    ) {
        var installation = await dbContext.GuildModuleInstallations
            .FirstOrDefaultAsync(x =>
                x.GuildInstance.DiscordGuildId == discordGuildId &&
                x.GuildInstance.IsActive &&
                x.ModuleName == moduleName, cancellationToken)
            ?? throw new InvalidOperationException($"Module '{moduleName}' is not installed for this guild.");

        if (string.IsNullOrWhiteSpace(installation.GitUrl))
            throw new InvalidOperationException("Module does not have a stored Git URL — cannot update.");

        var gitUrl = installation.GitUrl;
        var tempDir = ModuleBuildHelper.CreateTempDir();

        try {
            var (manifestJson, manifest) = await ModuleBuildHelper.CloneAndReadManifestAsync(gitUrl, tempDir, cancellationToken);
            ModuleBuildHelper.ValidateManifest(manifest, ModulePlatform.Discord);

            if (manifest.Name != moduleName)
                throw new InvalidOperationException($"Module name mismatch: expected '{moduleName}', got '{manifest.Name}'.");

            await ModuleBuildHelper.BuildAndCopyModuleAsync(manifest, tempDir, cancellationToken);

            installation.ModuleVersion = manifest.Version;
            installation.ModuleAuthor = manifest.Author;
            installation.EntryPoint = manifest.EntryPoint;
            installation.CanPerformActions = manifest.CanPerformActions;
            installation.ManifestJson = manifestJson;
            installation.UpdatedAtUtc = DateTime.UtcNow;

            await dbContext.SaveChangesAsync(cancellationToken);
            await cache.InvalidateAsync(discordGuildId);

            moduleRunnerService.UnloadModule(moduleName, manifest.Version);

            logger.LogInformation(
                "Module {ModuleName} updated to v{Version} for guild {GuildId}",
                manifest.Name, manifest.Version, discordGuildId);

            return installation;
        } finally {
            ModuleBuildHelper.CleanupTempDir(tempDir);
        }
    }

    public async Task SetEnabledAsync(
        long discordGuildId,
        string moduleName,
        bool isEnabled,
        CancellationToken cancellationToken = default
    ) {
        var installation = await dbContext.GuildModuleInstallations
            .FirstOrDefaultAsync(x =>
                x.GuildInstance.DiscordGuildId == discordGuildId &&
                x.GuildInstance.IsActive &&
                x.ModuleName == moduleName, cancellationToken)
            ?? throw new InvalidOperationException($"Module '{moduleName}' is not installed for this guild.");

        installation.IsEnabled = isEnabled;
        installation.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        await cache.InvalidateAsync(discordGuildId);
    }

    public async Task UpdateSettingsAsync(
        long discordGuildId,
        string moduleName,
        string settingsJson,
        CancellationToken cancellationToken = default
    ) {
        var installation = await dbContext.GuildModuleInstallations
            .FirstOrDefaultAsync(x =>
                x.GuildInstance.DiscordGuildId == discordGuildId &&
                x.GuildInstance.IsActive &&
                x.ModuleName == moduleName, cancellationToken)
            ?? throw new InvalidOperationException($"Module '{moduleName}' is not installed for this guild.");

        installation.SettingsJson = settingsJson;
        installation.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        await cache.InvalidateAsync(discordGuildId);

        await moduleRunnerService.ReloadModuleSettingsAsync(
            moduleName, installation.ModuleVersion, settingsJson, cancellationToken);
    }

    public async Task<IReadOnlyList<GuildModuleInstallation>> ListInstalledAsync(
        long discordGuildId,
        CancellationToken cancellationToken = default
    ) {
        return await dbContext.GuildModuleInstallations
            .Where(x => x.GuildInstance.DiscordGuildId == discordGuildId && x.GuildInstance.IsActive)
            .ToListAsync(cancellationToken);
    }
}
