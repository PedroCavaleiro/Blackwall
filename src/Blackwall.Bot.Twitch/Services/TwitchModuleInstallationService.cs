using Blackwall.Core.Entities;
using Blackwall.Infrastructure.Cache.Twitch;
using Blackwall.Infrastructure.Persistence;
using Blackwall.Modules.Abstractions;
using Blackwall.Modules.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Blackwall.Bot.Twitch.Services;

public sealed class TwitchModuleInstallationService(
    BlackwallDbContext dbContext,
    TwitchModuleInstallationCache cache,
    TwitchModuleRunnerService moduleRunnerService,
    ILogger<TwitchModuleInstallationService> logger
) {
    private static readonly JsonSerializerOptions JsonOptions = ModuleBuildHelper.JsonOptions;

    public async Task<TwitchChannelModuleInstallation> InstallAsync(
        long twitchUserId,
        string gitUrl,
        CancellationToken cancellationToken = default
    ) {
        if (!Uri.TryCreate(gitUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Git URL must be a valid HTTPS URL.");

        var channelInstance = await dbContext.TwitchChannelInstances
            .FirstOrDefaultAsync(x => x.TwitchUserId == twitchUserId && x.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("Twitch channel not found or not active.");

        var tempDir = ModuleBuildHelper.CreateTempDir();

        try {
            var (manifestJson, manifest) = await ModuleBuildHelper.CloneAndReadManifestAsync(gitUrl, tempDir, cancellationToken);
            ModuleBuildHelper.ValidateManifest(manifest, ModulePlatform.Twitch);

            var existing = await dbContext.TwitchChannelModuleInstallations
                .FirstOrDefaultAsync(x => x.TwitchChannelInstanceId == channelInstance.Id && x.ModuleName == manifest.Name, cancellationToken);

            if (existing is not null)
                throw new InvalidOperationException($"Module '{manifest.Name}' is already installed for this channel.");

            await ModuleBuildHelper.BuildAndCopyModuleAsync(manifest, tempDir, cancellationToken);

            var defaultSettings = ModuleBuildHelper.BuildDefaultSettings(manifest);

            var installation = new TwitchChannelModuleInstallation {
                TwitchChannelInstanceId = channelInstance.Id,
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

            dbContext.TwitchChannelModuleInstallations.Add(installation);
            await dbContext.SaveChangesAsync(cancellationToken);

            await cache.InvalidateAsync(twitchUserId);

            logger.LogInformation(
                "Module {ModuleName} v{Version} installed for Twitch channel {TwitchUserId}",
                manifest.Name, manifest.Version, twitchUserId);

            return installation;
        } finally {
            ModuleBuildHelper.CleanupTempDir(tempDir);
        }
    }

    public async Task UninstallAsync(
        long twitchUserId,
        string moduleName,
        CancellationToken cancellationToken = default
    ) {
        var installation = await dbContext.TwitchChannelModuleInstallations
            .FirstOrDefaultAsync(x =>
                x.TwitchChannelInstance.TwitchUserId == twitchUserId &&
                x.TwitchChannelInstance.IsActive &&
                x.ModuleName == moduleName, cancellationToken)
            ?? throw new InvalidOperationException($"Module '{moduleName}' is not installed for this channel.");

        dbContext.TwitchChannelModuleInstallations.Remove(installation);
        await dbContext.SaveChangesAsync(cancellationToken);

        await cache.InvalidateAsync(twitchUserId);

        logger.LogInformation(
            "Module {ModuleName} uninstalled from Twitch channel {TwitchUserId}",
            moduleName, twitchUserId);
    }

    public async Task<TwitchChannelModuleInstallation> UpdateAsync(
        long twitchUserId,
        string moduleName,
        CancellationToken cancellationToken = default
    ) {
        var installation = await dbContext.TwitchChannelModuleInstallations
            .FirstOrDefaultAsync(x =>
                x.TwitchChannelInstance.TwitchUserId == twitchUserId &&
                x.TwitchChannelInstance.IsActive &&
                x.ModuleName == moduleName, cancellationToken)
            ?? throw new InvalidOperationException($"Module '{moduleName}' is not installed for this channel.");

        if (string.IsNullOrWhiteSpace(installation.GitUrl))
            throw new InvalidOperationException("Module does not have a stored Git URL — cannot update.");

        var gitUrl = installation.GitUrl;
        var tempDir = ModuleBuildHelper.CreateTempDir();

        try {
            var (manifestJson, manifest) = await ModuleBuildHelper.CloneAndReadManifestAsync(gitUrl, tempDir, cancellationToken);
            ModuleBuildHelper.ValidateManifest(manifest, ModulePlatform.Twitch);

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
            await cache.InvalidateAsync(twitchUserId);

            moduleRunnerService.UnloadModule(moduleName, manifest.Version);

            logger.LogInformation(
                "Module {ModuleName} updated to v{Version} for Twitch channel {TwitchUserId}",
                manifest.Name, manifest.Version, twitchUserId);

            return installation;
        } finally {
            ModuleBuildHelper.CleanupTempDir(tempDir);
        }
    }

    public async Task SetEnabledAsync(
        long twitchUserId,
        string moduleName,
        bool isEnabled,
        CancellationToken cancellationToken = default
    ) {
        var installation = await dbContext.TwitchChannelModuleInstallations
            .FirstOrDefaultAsync(x =>
                x.TwitchChannelInstance.TwitchUserId == twitchUserId &&
                x.TwitchChannelInstance.IsActive &&
                x.ModuleName == moduleName, cancellationToken)
            ?? throw new InvalidOperationException($"Module '{moduleName}' is not installed for this channel.");

        if (isEnabled) {
            var dllPath = Path.Combine(ModuleBuildHelper.ModulesBasePath, installation.ModuleName, installation.ModuleVersion, installation.EntryPoint);
            if (!File.Exists(dllPath) || !ModuleCompatibilityService.IsAssemblyCompatible(dllPath)) {
                var referencedVersion = File.Exists(dllPath)
                    ? ModuleCompatibilityService.GetAbstractionsReferenceVersion(dllPath)
                    : null;
                throw new InvalidOperationException(
                    $"Module '{moduleName}' cannot be enabled because it is incompatible with the current Blackwall runtime "
                    + $"(references Abstractions v{referencedVersion?.ToString() ?? "not found"}, "
                    + $"runtime has v{ModuleCompatibilityService.CurrentAbstractionsVersion}). "
                    + "Update the module to a compatible version first.");
            }
            installation.DisabledReason = null;
        } else {
            installation.DisabledReason = "Disabled by user.";
        }

        installation.IsEnabled = isEnabled;
        installation.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        await cache.InvalidateAsync(twitchUserId);
    }

    public async Task UpdateSettingsAsync(
        long twitchUserId,
        string moduleName,
        string settingsJson,
        CancellationToken cancellationToken = default
    ) {
        var installation = await dbContext.TwitchChannelModuleInstallations
            .FirstOrDefaultAsync(x =>
                x.TwitchChannelInstance.TwitchUserId == twitchUserId &&
                x.TwitchChannelInstance.IsActive &&
                x.ModuleName == moduleName, cancellationToken)
            ?? throw new InvalidOperationException($"Module '{moduleName}' is not installed for this channel.");

        installation.SettingsJson = settingsJson;
        installation.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        await cache.InvalidateAsync(twitchUserId);

        await moduleRunnerService.ReloadModuleSettingsAsync(
            moduleName, installation.ModuleVersion, settingsJson, cancellationToken);
    }

    public async Task<IReadOnlyList<TwitchChannelModuleInstallation>> ListInstalledAsync(
        long twitchUserId,
        CancellationToken cancellationToken = default
    ) {
        return await dbContext.TwitchChannelModuleInstallations
            .Where(x => x.TwitchChannelInstance.TwitchUserId == twitchUserId && x.TwitchChannelInstance.IsActive)
            .ToListAsync(cancellationToken);
    }
}
