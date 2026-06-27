using Microsoft.Extensions.Configuration;

namespace Blackwall.Core.Configuration;

public class WebOptions {
    public const string SectionName = "WEB";

    [ConfigurationKeyName("BASEURL")]
    public string BaseUrl { get; set; } = null!;
}