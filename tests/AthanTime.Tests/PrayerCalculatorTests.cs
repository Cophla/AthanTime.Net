using AthanTime.Net.Models;
using AthanTime.Net.Services;
using Batoulapps.Adhan;
using Batoulapps.Adhan.Internal;
using CalculationMethod = AthanTime.Net.Models.CalculationMethod;
using HighLatitudeRule = AthanTime.Net.Models.HighLatitudeRule;
using Prayer = AthanTime.Net.Models.Prayer;

namespace AthanTime.Tests;

/// <summary>Verifies supported presets and the dependency-injected Adhan calculation adapter.</summary>
public sealed class PrayerCalculatorTests(IPrayerCalculator prayerCalculator)
{
    private readonly IPrayerCalculator _prayerCalculator = prayerCalculator;

    /// <summary>Checks chronology, qibla normalization, and solar-noon sanity in multiple hemispheres.</summary>
    [Theory]
    [InlineData("Makkah", "SA", 21.3891, 39.8579, "Asia/Riyadh", "Makkah", 2026, 9, 20)]
    [InlineData("London", "GB", 51.5074, -0.1278, "Europe/London", "MWL", 2026, 6, 21)]
    [InlineData("Sydney", "AU", -33.8688, 151.2093, "Australia/Sydney", "MWL", 2026, 12, 21)]
    public void Calculate_ReturnsOrderedLocalTimes(
        string city,
        string countryCode,
        double latitude,
        double longitude,
        string timeZoneId,
        string methodId,
        int year,
        int month,
        int day)
    {
        var result = _prayerCalculator.Calculate(
            new DateOnly(year, month, day),
            new LocationInfo(city, city, countryCode, latitude, longitude, timeZoneId),
            CalculationMethod.Find(methodId),
            AsrJuristic.Standard,
            HighLatitudeRule.AngleBased);

        var values = result.Times.Select(item => item.LocalTime).ToArray();
        Assert.True(values.SequenceEqual(values.OrderBy(value => value)));
        Assert.InRange(result.Times.Single(item => item.Prayer == Prayer.Dhuhr).LocalTime.Hour, 10, 14);
        Assert.InRange(result.QiblaBearing, 0, 359.999999);
    }

    /// <summary>Checks the library-backed qibla value against the known Beirut direction.</summary>
    [Fact]
    public void Calculate_ReturnsExpectedBeirutQibla()
    {
        var result = _prayerCalculator.Calculate(
            new DateOnly(2026, 9, 20),
            new LocationInfo("beirut", "Beirut", "LB", 33.8938, 35.5018, "Asia/Beirut"),
            CalculationMethod.Find("mwl"),
            AsrJuristic.Standard,
            HighLatitudeRule.AngleBased);

        Assert.InRange(result.QiblaBearing, 161.3, 162.3);
    }

    /// <summary>Proves that the app adds no hidden Ramadan correction to Adhan's Umm al-Qura Isha.</summary>
    [Fact]
    public void Calculate_MakkahRamadanIsha_MatchesRawAdhanPreset()
    {
        var date = new DateOnly(2026, 2, 20);
        var location = new LocationInfo("makkah", "Makkah", "SA", 21.3891, 39.8579, "Asia/Riyadh");
        var app = _prayerCalculator.Calculate(
            date, location, AthanTime.Net.Models.CalculationMethod.Find("Makkah"),
            AsrJuristic.Standard, HighLatitudeRule.AngleBased);
        var raw = new Batoulapps.Adhan.PrayerTimes(
            new Batoulapps.Adhan.Coordinates(location.Latitude, location.Longitude),
            new DateComponents(date.Year, date.Month, date.Day),
            Batoulapps.Adhan.CalculationMethod.UMM_AL_QURA.GetParameters());
        var expected = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(raw.Isha, DateTimeKind.Utc),
            TimeZoneInfo.FindSystemTimeZoneById(location.TimeZoneId));

        Assert.Equal(expected, app.Times.Single(item => item.Prayer == Prayer.Isha).LocalTime);
    }

    /// <summary>Verifies that sunrise receives the same explicit minute-adjustment support as prayer rows.</summary>
    [Fact]
    public void Calculate_SunriseAdjustment_IsAppliedExplicitly()
    {
        var date = new DateOnly(2026, 9, 20);
        var location = new LocationInfo("sidon", "Sidon", "LB", 33.5631, 35.3689, "Asia/Beirut");
        var method = CalculationMethod.Find("Egypt");
        var raw = _prayerCalculator.Calculate(
            date, location, method, AsrJuristic.Standard, HighLatitudeRule.AngleBased);
        var adjusted = _prayerCalculator.Calculate(
            date, location, method, AsrJuristic.Standard, HighLatitudeRule.AngleBased,
            new Dictionary<Prayer, int> { [Prayer.Sunrise] = 7 });

        Assert.Equal(
            TimeSpan.FromMinutes(7),
            adjusted.Times.Single(item => item.Prayer == Prayer.Sunrise).LocalTime
                - raw.Times.Single(item => item.Prayer == Prayer.Sunrise).LocalTime);
    }

    /// <summary>
    /// Documents the per-prayer differences from the supplied IslamicFinder Sidon screenshot
    /// and proves that explicit user corrections reproduce it without changing Egyptian astronomy.
    /// </summary>
    [Fact]
    public void Calculate_SidonIslamicFinderReference_IsMatchedByExplicitCorrections()
    {
        var date = new DateOnly(2026, 9, 20);
        var location = new LocationInfo(
            "sidon-islamicfinder-reference", "Sidon", "LB", 33.5631, 35.3689, "Asia/Beirut");
        var method = CalculationMethod.Find("Egypt");
        var islamicFinder = new Dictionary<Prayer, TimeOnly>
        {
            [Prayer.Fajr] = new(4, 56),
            [Prayer.Sunrise] = new(6, 25),
            [Prayer.Dhuhr] = new(12, 33),
            [Prayer.Asr] = new(16, 1),
            [Prayer.Maghrib] = new(18, 44),
            [Prayer.Isha] = new(19, 59)
        };
        var islamicFinderManualCorrections = new Dictionary<Prayer, int>
        {
            [Prayer.Maghrib] = 5
        };
        var calculationDifferences = new Dictionary<Prayer, int>
        {
            [Prayer.Fajr] = 4,
            [Prayer.Asr] = 1,
            [Prayer.Maghrib] = 1,
            [Prayer.Isha] = -2
        };
        var correctionsForThisApp = Enum.GetValues<Prayer>().ToDictionary(
            prayer => prayer,
            prayer => islamicFinderManualCorrections.GetValueOrDefault(prayer)
                + calculationDifferences.GetValueOrDefault(prayer));

        var raw = _prayerCalculator.Calculate(
            date, location, method, AsrJuristic.Standard, HighLatitudeRule.AngleBased);
        var adjusted = _prayerCalculator.Calculate(
            date, location, method, AsrJuristic.Standard, HighLatitudeRule.AngleBased, correctionsForThisApp);

        foreach (var prayerTime in raw.Times)
        {
            var configuredCorrection = islamicFinderManualCorrections.GetValueOrDefault(prayerTime.Prayer);
            var islamicFinderRaw = islamicFinder[prayerTime.Prayer].AddMinutes(-configuredCorrection);
            Assert.Equal(
                TimeSpan.FromMinutes(calculationDifferences.GetValueOrDefault(prayerTime.Prayer)),
                islamicFinderRaw.ToTimeSpan() - TimeOnly.FromDateTime(prayerTime.LocalTime).ToTimeSpan());
        }

        foreach (var prayerTime in adjusted.Times)
            Assert.Equal(islamicFinder[prayerTime.Prayer], TimeOnly.FromDateTime(prayerTime.LocalTime));
    }

    /// <summary>Checks mixed-case identifiers.</summary>
    [Theory]
    [InlineData("mwl", "MWL")]
    [InlineData("MaKkAh", "Makkah")]
    [InlineData("MOONSIGHTING", "Moonsighting")]
    public void Find_IsCaseInsensitive(string input, string expected) =>
        Assert.Equal(expected, CalculationMethod.Find(input).Id, ignoreCase: true);

    /// <summary>Ensures explicitly non-Sunni Tehran and Jafari presets are not exposed.</summary>
    [Fact]
    public void All_ExcludesTehranAndJafari()
    {
        Assert.DoesNotContain(CalculationMethod.All,
            method => method.Id.Equals("Tehran", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(CalculationMethod.All,
            method => method.Id.Equals("Jafari", StringComparison.OrdinalIgnoreCase));
    }
}
