using Blackwall.Modules.Abstractions;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Blackwall.Modules.Runtime;

public static class ModuleCompatibilityService {
    private const string AbstractionsAssemblyName = "Blackwall.Modules.Abstractions";

    public static string CurrentAbstractionsVersion { get; } =
        typeof(IBlackwallModule).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";

    public static Version? GetAbstractionsReferenceVersion(string dllPath) {
        if (!File.Exists(dllPath))
            return null;

        using var stream = File.OpenRead(dllPath);
        using var peReader = new PEReader(stream);

        if (!peReader.HasMetadata)
            return null;

        var reader = peReader.GetMetadataReader();

        foreach (var refHandle in reader.AssemblyReferences) {
            var assemblyRef = reader.GetAssemblyReference(refHandle);
            var name = reader.GetString(assemblyRef.Name);

            if (string.Equals(name, AbstractionsAssemblyName, StringComparison.OrdinalIgnoreCase))
                return assemblyRef.Version;
        }

        return null;
    }

    public static bool IsAssemblyCompatible(string dllPath) {
        var referencedVersion = GetAbstractionsReferenceVersion(dllPath);
        if (referencedVersion is null)
            return false;

        var current = typeof(IBlackwallModule).Assembly.GetName().Version;
        if (current is null)
            return false;

        return current.Major == referencedVersion.Major && current.Minor == referencedVersion.Minor;
    }
}
