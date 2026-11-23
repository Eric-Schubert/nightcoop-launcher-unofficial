using System.IO;
using System.Reflection;
using System.Text.Json;
using NightCoopLauncher.App.Localization;
using NightCoopLauncher.Core;
using Xunit;

namespace NightCoopLauncher.App.Tests;

[Collection(SharedLocalizer.Name)]
public class LocalizerTests
{
    private static Dictionary<string, string> Load(string code)
    {
        var assembly = typeof(Localizer).Assembly;
        using var stream = assembly.GetManifestResourceStream($"Languages/{code}.json")!;
        using var reader = new StreamReader(stream);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd())!;
    }

    private static IEnumerable<string> Languages() =>
        typeof(Localizer).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith("Languages/", StringComparison.Ordinal))
            .Select(name => name["Languages/".Length..^".json".Length]);

    [Fact]
    public void Offers_german_and_english()
    {
        Assert.Equal(["de", "en"], Localizer.Current.Languages.Select(language => language.Code).Order());
    }

    [Fact]
    public void Every_dictionary_has_the_same_keys_as_the_english_one()
    {
        var english = Load("en").Keys.Order().ToList();

        foreach (var code in Languages())
        {
            Assert.Equal(english, Load(code).Keys.Order());
        }
    }

    [Fact]
    public void Every_error_has_a_message()
    {
        var english = Load("en");

        foreach (var error in Enum.GetNames<LauncherError>())
        {
            Assert.Contains("error." + error, english.Keys);
        }
    }

    [Fact]
    public void Switches_the_language_and_falls_back_to_the_key_for_unknown_texts()
    {
        var localizer = Localizer.Current;

        localizer.SetLanguage("de");
        Assert.Equal("SPIELEN", localizer["nav.play"]);

        localizer.SetLanguage("en");
        Assert.Equal("PLAY", localizer["nav.play"]);
        Assert.Equal("no.such.key", localizer["no.such.key"]);
    }

    [Fact]
    public void Falls_back_to_english_for_a_language_without_a_dictionary()
    {
        Localizer.Current.SetLanguage("xx");

        Assert.Equal("en", Localizer.Current.LanguageCode);
    }

    [Fact]
    public void Starts_in_english_until_a_language_is_chosen()
    {
        Localizer.Current.SetLanguage("de");

        Localizer.Current.SetLanguage(null);

        Assert.Equal("en", Localizer.Current.LanguageCode);
    }

    [Fact]
    public void Embeds_the_dictionaries_under_the_names_the_localizer_reads()
    {
        Assert.Contains("Languages/de.json", Assembly.GetAssembly(typeof(Localizer))!.GetManifestResourceNames());
    }
}
