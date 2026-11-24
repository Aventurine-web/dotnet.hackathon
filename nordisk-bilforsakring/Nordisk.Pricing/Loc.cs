using System.Globalization;
using System.Text.Json;

namespace Nordisk.Pricing;

/// <summary>A value that is itself a translation key (e.g. a fuel type name).</summary>
public sealed record Ref(string Key);

/// <summary>Translations live in i18n/{code}.json, one flat file per language. English is the fallback.</summary>
public sealed class Loc
{
    public static readonly (string Code, string Name, string Culture)[] Languages =
    [
        ("sv", "Svenska", "sv-SE"), ("nb", "Norsk bokmål", "nb-NO"), ("nn", "Norsk nynorsk", "nn-NO"),
        ("da", "Dansk", "da-DK"), ("fi", "Suomi", "fi-FI"), ("is", "Íslenska", "is-IS"), ("en", "English", "en-GB"),
    ];

    private readonly Dictionary<string, Dictionary<string, string>> _tables = new();

    public Loc(string dir)
    {
        foreach (var l in Languages)
            _tables[l.Code] = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(dir, l.Code + ".json")))!;
    }

    public IReadOnlyDictionary<string, string> Table(string lang) => _tables[lang];
    public string Normalize(string? code) => Languages.Any(l => l.Code == code) ? code! : "en";

    public IReadOnlyDictionary<string, string> Ui(string lang) =>
        _tables["en"].Keys.Where(k => k.StartsWith("ui.")).ToDictionary(k => k, k => Get(lang, k));

    public string T(string lang, string key, IReadOnlyDictionary<string, object>? args = null)
    {
        var text = Get(lang, key);
        if (args is null) return text;
        var culture = CultureInfo.GetCultureInfo(Languages.First(l => l.Code == lang).Culture);
        foreach (var (name, value) in args)
        {
            var s = value switch
            {
                int i => i.ToString("N0", culture),
                Ref r => Get(lang, r.Key),
                _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
            };
            text = text.Replace("{" + name + "}", s);
        }
        return text;
    }

    private string Get(string lang, string key) =>
        _tables[lang].GetValueOrDefault(key) ?? _tables["en"].GetValueOrDefault(key) ?? key;
}
