using Blackwall.Modules.Abstractions;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Blackwall.Modules.Runtime;

public static class ModuleBuildHelper {
    public static readonly string ModulesBasePath = Path.Combine(AppContext.BaseDirectory, "modules");

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static void ValidateManifest(BlackwallModuleManifest manifest, ModulePlatform targetPlatform) {
        if (string.IsNullOrWhiteSpace(manifest.Name))
            throw new InvalidOperationException("Manifest must specify a name.");

        if (string.IsNullOrWhiteSpace(manifest.Version))
            throw new InvalidOperationException("Manifest must specify a version.");

        if (string.IsNullOrWhiteSpace(manifest.Author))
            throw new InvalidOperationException("Manifest must specify an author.");

        if (string.IsNullOrWhiteSpace(manifest.EntryPoint))
            throw new InvalidOperationException("Manifest must specify an entryPoint.");

        if (manifest.Platforms is null || manifest.Platforms.Count == 0)
            throw new InvalidOperationException("Manifest must specify at least one platform.");

        if (!manifest.Platforms.Contains(targetPlatform))
            throw new InvalidOperationException(
                $"Module '{manifest.Name}' does not support platform '{targetPlatform}'. Supported: {string.Join(", ", manifest.Platforms)}.");

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

    public static Dictionary<string, string?> BuildDefaultSettings(BlackwallModuleManifest manifest) {
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

    public static async Task<(string ManifestJson, BlackwallModuleManifest Manifest)> CloneAndReadManifestAsync(
        string gitUrl,
        string tempDir,
        CancellationToken ct
    ) {
        var exitCode = await RunGitCloneAsync(gitUrl, tempDir, ct);
        if (exitCode != 0)
            throw new InvalidOperationException($"git clone failed with exit code {exitCode}");

        var manifestPath = Path.Combine(tempDir, "blackwall-module.json");
        if (!File.Exists(manifestPath))
            throw new InvalidOperationException("blackwall-module.json not found in repository root.");

        var manifestJson = await File.ReadAllTextAsync(manifestPath, ct);
        var manifest = JsonSerializer.Deserialize<BlackwallModuleManifest>(manifestJson, JsonOptions)
            ?? throw new InvalidOperationException("Failed to parse blackwall-module.json");

        return (manifestJson, manifest);
    }

    public static async Task BuildAndCopyModuleAsync(
        BlackwallModuleManifest manifest,
        string tempDir,
        CancellationToken ct
    ) {
        var srcDir = Path.Combine(tempDir, "src");
        if (!Directory.Exists(srcDir))
            throw new InvalidOperationException("Repository must contain a 'src' directory with the module project.");

        var csprojFiles = Directory.GetFiles(srcDir, "*.csproj", SearchOption.TopDirectoryOnly);
        if (csprojFiles.Length == 0)
            throw new InvalidOperationException("No .csproj file found in src/ directory.");

        var (buildExitCode, buildOutput) = await RunDotnetBuildAsync(srcDir, ct);
        if (buildExitCode != 0)
            throw new InvalidOperationException($"dotnet build failed with exit code {buildExitCode}.\n{buildOutput}");

        var buildOutputDir = Path.Combine(srcDir, "bin", "Release", "net10.0");
        if (!Directory.Exists(buildOutputDir))
            throw new InvalidOperationException("Build output directory not found. Ensure the project targets net10.0.");

        var sourceDllPath = Path.Combine(buildOutputDir, manifest.EntryPoint);
        if (!File.Exists(sourceDllPath))
            throw new InvalidOperationException($"Entry point DLL '{manifest.EntryPoint}' was not produced by the build.");

        var moduleDir = Path.Combine(ModulesBasePath, manifest.Name, manifest.Version);
        Directory.CreateDirectory(moduleDir);

        var destDllPath = Path.Combine(moduleDir, manifest.EntryPoint);
        File.Copy(sourceDllPath, destDllPath, overwrite: true);

        var depsJsonPath = Path.ChangeExtension(sourceDllPath, ".deps.json");
        if (File.Exists(depsJsonPath))
            File.Copy(depsJsonPath, Path.ChangeExtension(destDllPath, ".deps.json"), overwrite: true);

        var runtimeConfigPath = Path.ChangeExtension(sourceDllPath, ".runtimeconfig.json");
        if (File.Exists(runtimeConfigPath))
            File.Copy(runtimeConfigPath, Path.ChangeExtension(destDllPath, ".runtimeconfig.json"), overwrite: true);
    }

    public static void CleanupTempDir(string tempDir) {
        try {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        } catch {
            // ignored
        }
    }

    public static string CreateTempDir() {
        var tempDir = Path.Combine(Path.GetTempPath(), $"blackwall-module-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        return tempDir;
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

    private static async Task<(int ExitCode, string Output)> RunDotnetBuildAsync(string srcDir, CancellationToken ct) {
        var dotnetHome = Path.Combine(Path.GetTempPath(), $"dotnet-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dotnetHome);

        var psi = new ProcessStartInfo {
            FileName = "dotnet",
            Arguments = "build -c Release",
            WorkingDirectory = srcDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            Environment = {
                ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "true",
                ["DOTNET_CLI_HOME"] = dotnetHome,
                ["NUGET_PACKAGES"] = Path.Combine(dotnetHome, "nuget-cache")
            }
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start dotnet build process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var output = string.IsNullOrEmpty(stderr) ? stdout : $"{stdout}\n{stderr}";

        try { if (Directory.Exists(dotnetHome)) Directory.Delete(dotnetHome, recursive: true); }
        catch {
            // ignored
        }

        return (process.ExitCode, output);
    }
}
