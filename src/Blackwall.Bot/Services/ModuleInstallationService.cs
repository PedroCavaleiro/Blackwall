using Blackwall.Core.Entities;
using Blackwall.Infrastructure.Cache;
using Blackwall.Infrastructure.Persistence;
using Blackwall.Modules.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Blackwall.Bot.Services;

public sealed class ModuleInstallationService(
    BlackwallDbContext dbContext,
    ModuleInstallationCache cache,
    ILogger<ModuleInstallationService> logger
) {
    private static readonly string ModulesBasePath = Path.Combine(AppContext.BaseDirectory, "modules");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

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

        var tempDir = Path.Combine(Path.GetTempPath(), $"blackwall-module-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try {
            var exitCode = await RunGitCloneAsync(gitUrl, tempDir, cancellationToken);
            if (exitCode != 0)
                throw new InvalidOperationException($"git clone failed with exit code {exitCode}");

            var manifestPath = Path.Combine(tempDir, "blackwall-module.json");
            if (!File.Exists(manifestPath))
                throw new InvalidOperationException("blackwall-module.json not found in repository root.");

            var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken);
            var manifest = JsonSerializer.Deserialize<BlackwallModuleManifest>(manifestJson, JsonOptions)
                ?? throw new InvalidOperationException("Failed to parse blackwall-module.json");

            ValidateManifest(manifest);

            var existing = await dbContext.GuildModuleInstallations
                .FirstOrDefaultAsync(x => x.GuildInstanceId == guildInstance.Id && x.ModuleName == manifest.Name, cancellationToken);

            if (existing is not null)
                throw new InvalidOperationException($"Module '{manifest.Name}' is already installed for this guild.");

            var moduleDir = Path.Combine(ModulesBasePath, manifest.Name, manifest.Version);
            Directory.CreateDirectory(moduleDir);

            var srcDir = Path.Combine(tempDir, "src");
            if (!Directory.Exists(srcDir))
                throw new InvalidOperationException("Repository must contain a 'src' directory with the module project.");

            var csprojFiles = Directory.GetFiles(srcDir, "*.csproj", SearchOption.TopDirectoryOnly);
            if (csprojFiles.Length == 0)
                throw new InvalidOperationException("No .csproj file found in src/ directory.");

            var buildExitCode = await RunDotnetBuildAsync(srcDir, cancellationToken);
            if (buildExitCode != 0)
                throw new InvalidOperationException($"dotnet build failed with exit code {buildExitCode}.");

            var buildOutputDir = Path.Combine(srcDir, "bin", "Release", "net10.0");
            if (!Directory.Exists(buildOutputDir))
                throw new InvalidOperationException("Build output directory not found. Ensure the project targets net10.0.");

            var sourceDllPath = Path.Combine(buildOutputDir, manifest.EntryPoint);
            if (!File.Exists(sourceDllPath))
                throw new InvalidOperationException($"Entry point DLL '{manifest.EntryPoint}' was not produced by the build.");

            var destDllPath = Path.Combine(moduleDir, manifest.EntryPoint);
            File.Copy(sourceDllPath, destDllPath, overwrite: true);

            var depsJsonPath = Path.ChangeExtension(sourceDllPath, ".deps.json");
            if (File.Exists(depsJsonPath))
                File.Copy(depsJsonPath, Path.ChangeExtension(destDllPath, ".deps.json"), overwrite: true);

            var runtimeConfigPath = Path.ChangeExtension(sourceDllPath, ".runtimeconfig.json");
            if (File.Exists(runtimeConfigPath))
                File.Copy(runtimeConfigPath, Path.ChangeExtension(destDllPath, ".runtimeconfig.json"), overwrite: true);

            var defaultSettings = BuildDefaultSettings(manifest);

            var installation = new GuildModuleInstallation {
                GuildInstanceId = guildInstance.Id,
                ModuleName = manifest.Name,
                ModuleVersion = manifest.Version,
                ModuleAuthor = manifest.Author,
                EntryPoint = manifest.EntryPoint,
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
            try {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            } catch {
                // ignored
            }
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
    }

    public async Task<IReadOnlyList<GuildModuleInstallation>> ListInstalledAsync(
        long discordGuildId,
        CancellationToken cancellationToken = default
    ) {
        return await dbContext.GuildModuleInstallations
            .Where(x => x.GuildInstance.DiscordGuildId == discordGuildId && x.GuildInstance.IsActive)
            .ToListAsync(cancellationToken);
    }

    private static void ValidateManifest(BlackwallModuleManifest manifest) {
        if (string.IsNullOrWhiteSpace(manifest.Name))
            throw new InvalidOperationException("Manifest must specify a name.");

        if (string.IsNullOrWhiteSpace(manifest.Version))
            throw new InvalidOperationException("Manifest must specify a version.");

        if (string.IsNullOrWhiteSpace(manifest.Author))
            throw new InvalidOperationException("Manifest must specify an author.");

        if (string.IsNullOrWhiteSpace(manifest.EntryPoint))
            throw new InvalidOperationException("Manifest must specify an entryPoint.");

        if (manifest.SettingsSchema is not null) {
            foreach (var card in manifest.SettingsSchema.Cards) {
                if (string.IsNullOrWhiteSpace(card.Title))
                    throw new InvalidOperationException("Each settings card must have a title.");

                foreach (var field in card.Fields) {
                    if (string.IsNullOrWhiteSpace(field.Key))
                        throw new InvalidOperationException("Each settings field must have a key.");

                    if (string.IsNullOrWhiteSpace(field.UiName))
                        throw new InvalidOperationException($"Field '{field.Key}' must have a uiName.");

                    if (field.InputType == ModuleInputType.Dropdown && (field.Options is null || field.Options.Count == 0))
                        throw new InvalidOperationException($"Field '{field.Key}' is a dropdown but has no options.");
                }
            }
        }
    }

    private static Dictionary<string, string?> BuildDefaultSettings(BlackwallModuleManifest manifest) {
        var settings = new Dictionary<string, string?>();

        if (manifest.SettingsSchema is null)
            return settings;

        foreach (var card in manifest.SettingsSchema.Cards) {
            foreach (var field in card.Fields) {
                settings[field.Key] = field.DefaultValue;
            }
        }

        return settings;
    }

    private static async Task<int> RunGitCloneAsync(string url, string targetDir, CancellationToken ct) {
        var psi = new ProcessStartInfo {
            FileName = "git",
            Arguments = $"clone --depth 1 \"{url}\" \"{targetDir}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start git process.");
        await process.WaitForExitAsync(ct);
        return process.ExitCode;
    }

    private static async Task<int> RunDotnetBuildAsync(string srcDir, CancellationToken ct) {
        var psi = new ProcessStartInfo {
            FileName = "dotnet",
            Arguments = $"build -c Release",
            WorkingDirectory = srcDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start dotnet build process.");
        await process.WaitForExitAsync(ct);
        return process.ExitCode;
    }
}
