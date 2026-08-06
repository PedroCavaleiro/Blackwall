// ReSharper disable MemberCanBePrivate.Global
namespace Blackwall.Modules.Abstractions;

public sealed class ModuleSettings(Dictionary<string, string?> values) {
    private readonly Dictionary<string, string?> _values = values ?? throw new ArgumentNullException(nameof(values));

    public string? Get(string key) =>
        _values.GetValueOrDefault(key);

    public int? GetInt32(string key) =>
        int.TryParse(Get(key), out var value) ? value : null;

    public bool? GetBoolean(string key) =>
        bool.TryParse(Get(key), out var value) ? value : null;

    public IReadOnlyDictionary<string, string?> Values => _values;
}
