using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace NightCoopLauncher.App.Localization;

public sealed record LanguageInfo(string Code, string Name);

/// <summary>
/// Looks texts up in per-language dictionaries (Languages/&lt;code&gt;.json). A key missing
/// from the active language falls back to English, so a new dictionary can be partial.
/// </summary>
public sealed class Localizer : INotifyPropertyChanged
{
    private const string ResourcePrefix = "Languages/";
    private const string ResourceSuffix = ".json";
    private const string FallbackCode = "en";
    private const string NameKey = "language.name";

    private readonly Dictionary<string, Dictionary<string, string>> _dictionaries = LoadDictionaries();
    private Dictionary<string, string> _active;

    private Localizer()
    {
        _active = _dictionaries[FallbackCode];
        Languages = _dictionaries
            .Select(entry => new LanguageInfo(entry.Key, entry.Value.GetValueOrDefault(NameKey, entry.Key)))
            .OrderBy(language => language.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    public static Localizer Current { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? LanguageChanged;

    public IReadOnlyList<LanguageInfo> Languages { get; }

    public string LanguageCode { get; private set; } = FallbackCode;

    public CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo(FallbackCode);

    public string this[string key] =>
        _active.TryGetValue(key, out var text) || _dictionaries[FallbackCode].TryGetValue(key, out text) ? text : key;

    public string Format(string key, params object?[] arguments)
    {
        try
        {
            return string.Format(Culture, this[key], arguments);
        }
        catch (FormatException)
        {
            // A translation with a wrong placeholder should look odd, not crash the launcher.
            return this[key];
        }
    }

    /// <param name="code">A language code, or null for the default language, English.</param>
    public void SetLanguage(string? code)
    {
        var resolved = code is not null && _dictionaries.ContainsKey(code) ? code : FallbackCode;

        LanguageCode = resolved;
        Culture = CultureInfo.GetCultureInfo(resolved);
        _active = _dictionaries[resolved];

        // "Item[]" is the name WPF listens to for indexer bindings.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private static Dictionary<string, Dictionary<string, string>> LoadDictionaries()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var dictionaries = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var resource in assembly.GetManifestResourceNames().Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            var code = resource[ResourcePrefix.Length..^ResourceSuffix.Length];
            dictionaries[code] = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd()) ?? [];
        }

        return dictionaries;
    }
}
