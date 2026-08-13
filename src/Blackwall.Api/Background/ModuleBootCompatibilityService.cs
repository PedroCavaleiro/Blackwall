using Blackwall.Infrastructure.Persistence;
using Blackwall.Modules.Abstractions;
using Blackwall.Modules.Runtime;
using Microsoft.EntityFrameworkCore;

namespace Blackwall.Api.Background;

public sealed class ModuleBootCompatibilityService(
    IServiceScopeFactory scopeFactory,
    ILogger<ModuleBootCompatibilityService> logger
) : IHostedService {

    public async Task StartAsync(CancellationToken cancellationToken) {
        logger.LogInformation(
            "Starting module compatibility check (Abstractions v{Version})",
            ModuleCompatibilityService.CurrentAbstractionsVersion);

        await CheckDiscordModulesAsync(cancellationToken);
        await CheckTwitchModulesAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task CheckDiscordModulesAsync(CancellationToken ct) {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BlackwallDbContext>();

        var installations = await dbContext.GuildModuleInstallations
            .Where(x => x.IsEnabled)
            .Include(x => x.GuildInstance)
            .ToListAsync(ct);

        if (installations.Count == 0)
            return;

        logger.LogInformation(
            "Checking {Count} Discord module installations for compatibility",
            installations.Count);

        var changed = false;

        foreach (var installation in installations) {
            var result = await CheckAndRepairAsync(
                installation.ModuleName,
                installation.ModuleVersion,
                installation.EntryPoint,
                installation.GitUrl,
                ModulePlatform.Discord,
                ct
            );

            switch (result) {
                case BootResult.Compatible:
                    break;
                case BootResult.Disabled d:
                    logger.LogWarning(
                        "Disabling Discord module {ModuleName} v{Version} for guild {GuildId}: {Reason}",
                        installation.ModuleName, installation.ModuleVersion,
                        installation.GuildInstance.DiscordGuildId, d.Reason);
                    installation.IsEnabled = false;
                    installation.DisabledReason = d.Reason;
                    installation.UpdatedAtUtc = DateTime.UtcNow;
                    changed = true;
                    break;
                case BootResult.Updated u:
                    installation.ModuleVersion = u.Version;
                    installation.ModuleAuthor = u.Author;
                    installation.EntryPoint = u.EntryPoint;
                    installation.CanPerformActions = u.CanPerformActions;
                    installation.ManifestJson = u.ManifestJson;
                    installation.DisabledReason = null;
                    installation.UpdatedAtUtc = DateTime.UtcNow;
                    logger.LogInformation(
                        "Discord module {ModuleName} auto-updated to v{Version}",
                        installation.ModuleName, u.Version);
                    changed = true;
                    break;
            }
        }

        if (changed)
            await dbContext.SaveChangesAsync(ct);
    }

    private async Task CheckTwitchModulesAsync(CancellationToken ct) {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BlackwallDbContext>();

        var installations = await dbContext.TwitchChannelModuleInstallations
            .Where(x => x.IsEnabled)
            .Include(x => x.TwitchChannelInstance)
            .ToListAsync(ct);

        if (installations.Count == 0)
            return;

        logger.LogInformation(
            "Checking {Count} Twitch module installations for compatibility",
            installations.Count);

        var changed = false;

        foreach (var installation in installations) {
            var result = await CheckAndRepairAsync(
                installation.ModuleName,
                installation.ModuleVersion,
                installation.EntryPoint,
                installation.GitUrl,
                ModulePlatform.Twitch,
                ct
            );

            switch (result) {
                case BootResult.Compatible:
                    break;
                case BootResult.Disabled d:
                    logger.LogWarning(
                        "Disabling Twitch module {ModuleName} v{Version} for channel {TwitchUserId}: {Reason}",
                        installation.ModuleName, installation.ModuleVersion,
                        installation.TwitchChannelInstance.TwitchUserId, d.Reason);
                    installation.IsEnabled = false;
                    installation.DisabledReason = d.Reason;
                    installation.UpdatedAtUtc = DateTime.UtcNow;
                    changed = true;
                    break;
                case BootResult.Updated u:
                    installation.ModuleVersion = u.Version;
                    installation.ModuleAuthor = u.Author;
                    installation.EntryPoint = u.EntryPoint;
                    installation.CanPerformActions = u.CanPerformActions;
                    installation.ManifestJson = u.ManifestJson;
                    installation.DisabledReason = null;
                    installation.UpdatedAtUtc = DateTime.UtcNow;
                    logger.LogInformation(
                        "Twitch module {ModuleName} auto-updated to v{Version}",
                        installation.ModuleName, u.Version);
                    changed = true;
                    break;
            }
        }

        if (changed)
            await dbContext.SaveChangesAsync(ct);
    }

    private async Task<BootResult> CheckAndRepairAsync(
        string moduleName,
        string moduleVersion,
        string entryPoint,
        string gitUrl,
        ModulePlatform platform,
        CancellationToken ct
    ) {
        var dllPath = Path.Combine(ModuleBuildHelper.ModulesBasePath, moduleName, moduleVersion, entryPoint);

        if (File.Exists(dllPath) && ModuleCompatibilityService.IsAssemblyCompatible(dllPath))
            return new BootResult.Compatible();

        var referencedVersion = File.Exists(dllPath)
            ? ModuleCompatibilityService.GetAbstractionsReferenceVersion(dllPath)
            : null;

        logger.LogWarning(
            "Module {ModuleName} v{Version} is incompatible (references Abstractions v{Referenced}, runtime has v{Current}) — attempting update",
            moduleName, moduleVersion,
            referencedVersion?.ToString() ?? "(not found)",
            ModuleCompatibilityService.CurrentAbstractionsVersion);

        if (string.IsNullOrWhiteSpace(gitUrl))
            return new BootResult.Disabled("Module has no Git URL — cannot auto-update.");

        var tempDir = ModuleBuildHelper.CreateTempDir();
        try {
            var (newManifestJson, newManifest) = await ModuleBuildHelper.CloneAndReadManifestAsync(gitUrl, tempDir, ct);

            ModuleBuildHelper.ValidateManifest(newManifest, platform);

            if (newManifest.Name != moduleName)
                return new BootResult.Disabled($"Module name mismatch: expected '{moduleName}', got '{newManifest.Name}'.");

            await ModuleBuildHelper.BuildAndCopyModuleAsync(newManifest, tempDir, ct);

            return new BootResult.Updated(
                newManifestJson,
                newManifest.Version,
                newManifest.Author,
                newManifest.EntryPoint,
                newManifest.CanPerformActions
            );
        } catch (InvalidOperationException ex) {
            return new BootResult.Disabled($"Auto-update failed: {ex.Message}");
        } catch (Exception ex) {
            return new BootResult.Disabled($"Auto-update failed: {ex.Message}");
        } finally {
            ModuleBuildHelper.CleanupTempDir(tempDir);
        }
    }

    private abstract record BootResult {
        internal sealed record Compatible : BootResult;
        internal sealed record Disabled(string Reason) : BootResult;
        internal sealed record Updated(
            string ManifestJson,
            string Version,
            string Author,
            string EntryPoint,
            bool CanPerformActions
        ) : BootResult;
    }
}
