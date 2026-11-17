using System.Globalization;
using AthanTime.Net.Models;
using AthanTime.Net.Services;
using Microsoft.Extensions.Logging;

namespace AthanTime.Tests;

/// <summary>Verifies regional hints against the real offline catalog without changing machine culture or profiles.</summary>
public sealed class FirstRunDefaultsTests(ILocationRepository locations, ILogger<FirstRunDefaults> logger,
    ILogger<SettingsService> settingsLogger, ManualLocationService manual, NodaTime.IDateTimeZoneProvider timeZones)
{
    [Theory]
    [InlineData("ar-LB", "ar")]
    [InlineData("en-US", "en")]
    [InlineData("fr-FR", "fr")]
    [InlineData("es-ES", "es")]
    [InlineData("tr-TR", "tr")]
    [InlineData("de-DE", "de")]
    [InlineData("id-ID", "id")]
    [InlineData("ur-PK", "ur")]
    [InlineData("ja-JP", "en")]
    public void LanguageUsesSupportedParentAndCityRequiresConfirmation(string culture, string expected)
    {
        var settings = new AppSettings();
        new FirstRunDefaults(locations, timeZones, logger).Apply(settings, new CultureInfo(culture), new CultureInfo("fr-FR"), "LB", "Asia/Beirut");
        Assert.Equal(expected, settings.Language);
        Assert.True(settings.Use24HourClock);
        Assert.True(settings.LocationNeedsConfirmation);
        Assert.Equal("Beirut", locations.Get(settings.LocationId).City);
        Assert.Empty(settings.SummerTimeOverrides);
    }

    [Theory]
    [InlineData("Asia/Calcutta", "IN")]
    [InlineData("Europe/Vaduz", "LI")]
    [InlineData("Asia/Beirut", "US")]
    public void ZoneAliasesAndRegionPreferenceStayInsideMappedZone(string zone, string region)
    {
        var settings = new AppSettings();
        new FirstRunDefaults(locations, timeZones, logger).Apply(settings, new CultureInfo("en-US"), new CultureInfo("en-US"), region, zone);
        var place = locations.Get(settings.LocationId);
        Assert.Equal(region == "US" ? "LB" : region, place.CountryCode);
        var aliases = NodaTime.TimeZones.TzdbDateTimeZoneSource.Default.CanonicalIdMap;
        Assert.Equal(aliases[zone], aliases[place.TimeZoneId]);
        Assert.False(settings.Use24HourClock);
    }

    [Theory]
    [InlineData("missing/zone")]
    [InlineData("UTC")]
    public void UnavailableCityKeepsExplicitFallbackAndStillRequiresReview(string zone)
    {
        var settings = new AppSettings();
        new FirstRunDefaults(locations, timeZones, logger).Apply(settings, CultureInfo.InvariantCulture, CultureInfo.InvariantCulture, "", zone);
        Assert.Equal("geonames-276781", settings.LocationId);
        Assert.Equal("en", settings.Language);
        Assert.True(settings.LocationNeedsConfirmation);
    }

    [Fact]
    public void OnlyAbsentProfilesReceiveHintsAndLoadDoesNotWrite()
    {
        var directory = Directory.CreateTempSubdirectory("athan-first-run-");
        try
        {
            var path = Path.Combine(directory.FullName, "settings.json");
            int calls = 0;
            var store = new SettingsService(settingsLogger, manual, path, settings =>
            { calls++; settings.Language = "tr"; settings.LocationNeedsConfirmation = true; });
            var first = store.Load();
            Assert.Equal(1, calls);
            Assert.Empty(Directory.EnumerateFiles(directory.FullName));
            Assert.Equal("tr", first.Language);
            first.Language = "ar";
            first.LocationNeedsConfirmation = false;
            store.Save(first);
            Assert.Equal("ar", store.Load().Language);
            Assert.False(store.Load().LocationNeedsConfirmation);
            Assert.Equal(1, calls);
            File.WriteAllText(path, "broken");
            Assert.Equal("ar", store.Load().Language);
            Assert.Equal(1, calls);
            File.Delete(path + ".bak");
            store.Load();
            Assert.Equal(1, calls);
            File.WriteAllText(path, "{\"schemaVersion\":999}");
            store.Load();
            Assert.Equal(1, calls);
        }
        finally { directory.Delete(true); }
    }
}
