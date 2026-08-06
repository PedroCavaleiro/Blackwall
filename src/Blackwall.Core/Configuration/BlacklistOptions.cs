// ReSharper disable CollectionNeverUpdated.Global
namespace Blackwall.Core.Configuration;

public sealed class BlacklistOptions {
    public const string SectionName = "Blacklists";

    public List<string> Defaults { get; set; } = [];
}
