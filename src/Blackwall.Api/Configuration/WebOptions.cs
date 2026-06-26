namespace Blackwall.Api.Configuration;

public class WebOptions {
    public const string SectionName = "WEB";

    [ConfigurationKeyName("BASEURL")]
    public string BaseUrl { get; set; } = null!;
}