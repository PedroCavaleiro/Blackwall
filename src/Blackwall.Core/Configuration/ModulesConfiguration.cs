using Microsoft.Extensions.Configuration;
// ReSharper disable PropertyCanBeMadeInitOnly.Global

namespace Blackwall.Core.Configuration;

public sealed record ModulesConfiguration {
    public const string SectionName = "MODULES";

    [ConfigurationKeyName("REGISTRY_URL")]
    public string RegistryUrl { get; set; } = "https://raw.githubusercontent.com/PedroCavaleiro/Blackwall.Modules/main/index.json";

    [ConfigurationKeyName("REGISTRY_CACHE_MINUTES")]
    public int RegistryCacheMinutes { get; set; } = 15;

    [ConfigurationKeyName("ALLOW_THIRD_PARTY")]
    public bool AllowThirdParty { get; set; } = true;

    [ConfigurationKeyName("CATALOG_ONLY")]
    public bool CatalogOnly { get; set; } = true;
}
