using Microsoft.Extensions.Configuration;

namespace Blackwall.Core.Configuration;

public sealed class SafeBrowsingOptions {
    public const string SectionName = "SAFE_BROWSING";

    [ConfigurationKeyName("API_KEY")]
    public string? ApiKey { get; set; }

    [ConfigurationKeyName("BASE_URL")]
    public string BaseUrl { get; set; } = "https://safebrowsing.googleapis.com/v5";
}
