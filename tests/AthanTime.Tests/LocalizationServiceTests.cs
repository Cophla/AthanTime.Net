using AthanTime.Net.Services;
using System.Text.Json;

namespace AthanTime.Tests;

/// <summary>Verifies complete multilingual and case-insensitive text lookup.</summary>
public sealed class LocalizationServiceTests(ILocalizationService localizationService)
{
    private readonly ILocalizationService _localizationService = localizationService;

    /// <summary>Requires every JSON caption in all languages, not English/key fallback.</summary>
    [Fact]
    public void JsonCaptionsAreCompleteInAllEightLanguages()
    {
        using var stream = typeof(LocalizationService).Assembly.GetManifestResourceStream("AthanTime.Net.Data.ui-text.json")!;
        using var json = JsonDocument.Parse(stream);
        var languages = json.RootElement.EnumerateObject().ToDictionary(language => language.Name,
            language => language.Value.EnumerateObject().ToDictionary(pair => pair.Name, pair => pair.Value.GetString(), StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        var keys = languages["en"].Keys.ToArray();
        Assert.Contains("HelpText", keys);
        Assert.Contains("AboutText", keys);
        Assert.Contains("AppName", keys);
        Assert.Equal(LocalizationService.Languages.Select(language => language.Code).Order(StringComparer.Ordinal),
            languages.Keys.Order(StringComparer.Ordinal));
        foreach (var language in LocalizationService.Languages)
        {
            Assert.Equal(keys.Order(StringComparer.Ordinal), languages[language.Code].Keys.Order(StringComparer.Ordinal));
            foreach (var key in keys)
            {
                Assert.True(languages[language.Code].TryGetValue(key, out var value), $"Missing {key} in {language.Code}");
                Assert.False(string.IsNullOrWhiteSpace(value));
                Assert.Equal(value, _localizationService.Get(language.Code.ToUpperInvariant(), key.ToLowerInvariant()));
            }
        }
    }

    [Theory]
    [InlineData("xx", "AppName")]
    [InlineData("en-US", "SunriseReminder")]
    [InlineData("", "AboutText")]
    public void UnknownLanguagesUseEnglish(string language, string key) =>
        Assert.Equal(_localizationService.Get("en", key), _localizationService.Get(language, key));

    [Fact]
    public void UnknownKeysRemainVisible()
    {
        const string key = "Unknown.Caption";
        foreach (var language in LocalizationService.Languages)
            Assert.Equal(key, _localizationService.Get(language.Code, key));
        Assert.Equal(key, _localizationService.Get("unknown-language", key));
    }

    /// <summary>Checks that every language provides each text used by the main screen.</summary>
    [Fact]
    public void Get_ReturnsEveryUiKeyInEveryLanguage()
    {
        string[] keys =
        [
            "AppName", "Tagline", "NextPrayer", "Today", "Location", "SearchPlace", "Settings",
            "Method", "Asr", "HighLatitude", "Clock", "DarkMode", "Qibla", "Midnight", "Hijri", "Previous",
            "Next", "Fajr", "Sunrise", "Dhuhr", "AsrPrayer", "Maghrib", "Isha", "Standard",
            "Hanafi", "AngleBased", "MiddleOfNight", "OneSeventh", "None", "Coordinates",
            "Direction", "CloseToTray", "NoResults", "TrayShow", "TrayExit", "Tomorrow", "Offline",
            "Notifications", "Adjustments", "HijriAdjustment", "AutomaticConvention", "Convention",
            "NotifyBefore", "NotifyAtTime", "NotifyAfter", "Minutes", "CurrentPrayer", "PrayerReminder",
            "BeforePrayerMessage", "AtPrayerMessage", "AfterPrayerMessage", "Help", "About", "OK",
            "ManualOverride", "HelpText", "AboutText"
        ];

        foreach (var language in LocalizationService.Languages)
        foreach (var key in keys)
            Assert.False(string.IsNullOrWhiteSpace(_localizationService.Get(language.Code, key)));

        foreach (var language in LocalizationService.Languages)
            Assert.NotEqual("Adjustments", _localizationService.Get(language.Code, "Adjustments"));
    }

    /// <summary>Protects sunrise captions from being translated back into prayer labels.</summary>
    [Theory]
    [InlineData("en", "Sunrise reminder", "pray")]
    [InlineData("ar", "تذكير بالشروق", "صلاة")]
    [InlineData("fr", "Rappel du lever du soleil", "prière")]
    [InlineData("es", "Recordatorio de la salida del sol", "oración")]
    [InlineData("tr", "Güneş doğuşu hatırlatıcısı", "namaz")]
    [InlineData("de", "Erinnerung an den Sonnenaufgang", "Gebet")]
    [InlineData("id", "Pengingat matahari terbit", "salat")]
    [InlineData("ur", "طلوع آفتاب کی یاد دہانی", "نماز")]
    public void SunriseCaptionsDescribeATimeMarker(string language, string expectedTitle, string prayerWord)
    {
        Assert.Equal(expectedTitle, _localizationService.Get(language, "SunriseReminder"));
        Assert.NotEqual(_localizationService.Get(language, "PrayerReminder"), expectedTitle);
        string[] keys = ["NotifyBeforeSunrise", "NotifyAtSunrise", "NotifyAfterSunrise", "AtSunriseTiming",
            "SunriseReminder", "BeforeSunriseMessage", "AtSunriseMessage", "AfterSunriseMessage"];
        foreach (string key in keys)
        {
            string text = _localizationService.Get(language, key);
            Assert.NotEqual(key, text);
            Assert.False(text.Contains(prayerWord, StringComparison.OrdinalIgnoreCase), $"Prayer wording in {language}/{key}: {text}");
        }
    }

    /// <summary>Checks case-insensitive language and translation-key matching.</summary>
    [Fact]
    public void Get_IsCaseInsensitive()
    {
        Assert.Equal(
            _localizationService.Get("en", "AppName"),
            _localizationService.Get("EN", "appname"));
        Assert.Equal("أوقات الأذان", _localizationService.Get("AR", "APPNAME"));
    }
}
