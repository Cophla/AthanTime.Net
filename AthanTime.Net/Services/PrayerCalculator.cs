using Batoulapps.Adhan;
using Batoulapps.Adhan.Internal;
using AthanTime.Net.Models;
using Microsoft.Extensions.Logging;
using AdhanCalculationMethod = Batoulapps.Adhan.CalculationMethod;
using AdhanHighLatitudeRule = Batoulapps.Adhan.HighLatitudeRule;
using AppCalculationMethod = AthanTime.Net.Models.CalculationMethod;
using AppHighLatitudeRule = AthanTime.Net.Models.HighLatitudeRule;
using AppPrayer = AthanTime.Net.Models.Prayer;

namespace AthanTime.Net.Services;

/// <summary>
/// Defines the application's prayer-time calculation contract.
/// </summary>
public interface IPrayerCalculator
{
    /// <summary>Calculates a complete prayer schedule for one local civil date.</summary>
    DailyPrayerTimes Calculate(
        DateOnly date,
        LocationInfo location,
        AppCalculationMethod method,
        AsrJuristic asr,
        AppHighLatitudeRule highLatitudeRule,
        IReadOnlyDictionary<AppPrayer, int>? adjustments = null,
        SummerTimeSettings? summerTime = null);
}

/// <summary>
/// Calculates prayer times with the open-source Adhan engine and converts its UTC results
/// to the selected location's civil time.
/// </summary>
public sealed class PrayerCalculator(ILogger<PrayerCalculator> logger, TimeZoneService timeZones) : IPrayerCalculator
{
    private readonly ILogger<PrayerCalculator> _logger = logger;
    private readonly TimeZoneService _timeZones = timeZones;

    /// <inheritdoc />
    public DailyPrayerTimes Calculate(
        DateOnly date,
        LocationInfo location,
        AppCalculationMethod method,
        AsrJuristic asr,
        AppHighLatitudeRule highLatitudeRule,
        IReadOnlyDictionary<AppPrayer, int>? adjustments = null,
        SummerTimeSettings? summerTime = null)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(method);

        var timeZone = _timeZones.Find(location.TimeZoneId);
        var coordinates = new Coordinates(location.Latitude, location.Longitude);
        var parameters = CreateParameters(method);
        parameters.Madhab = asr == AsrJuristic.Hanafi ? Madhab.HANAFI : Madhab.SHAFI;
        parameters.HighLatitudeRule = MapHighLatitudeRule(highLatitudeRule);
        ApplyAdjustments(parameters, adjustments);

        var calculated = CalculateAdhan(date, coordinates, parameters);
        PrayerTime Event(AppPrayer prayer, DateTime utc) => new(prayer,
            _timeZones.ToLocal(new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)), timeZone, summerTime),
            new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)));
        var maghrib = Event(AppPrayer.Maghrib, calculated.Maghrib);
        var times = new[]
        {
            Event(AppPrayer.Fajr, calculated.Fajr),
            Event(AppPrayer.Sunrise, calculated.Sunrise),
            Event(AppPrayer.Dhuhr, calculated.Dhuhr),
            Event(AppPrayer.Asr, calculated.Asr),
            maghrib,
            Event(AppPrayer.Isha, calculated.Isha)
        };

        var nextDate = date.AddDays(1);
        var nextParameters = CreateParameters(method);
        nextParameters.Madhab = parameters.Madhab;
        nextParameters.HighLatitudeRule = parameters.HighLatitudeRule;
        ApplyAdjustments(nextParameters, adjustments);
        var nextCalculated = CalculateAdhan(nextDate, coordinates, nextParameters);
        var midnightEnd = new DateTimeOffset(DateTime.SpecifyKind(nextCalculated.Sunrise, DateTimeKind.Utc));
        var midnightUtc = maghrib.UtcTime!.Value.AddTicks((midnightEnd - maghrib.UtcTime.Value).Ticks / 2);
        var midnight = _timeZones.ToLocal(midnightUtc, timeZone, summerTime);
        var qibla = new Qibla(coordinates);

        _logger.LogDebug(
            "Calculated {Method} prayer times for {LocationId} on {Date} using {TimeZoneId}",
            method.Id,
            location.Id,
            date,
            location.TimeZoneId);

        return new DailyPrayerTimes(date, times, midnight, qibla.Direction);
    }

    /// <summary>Creates one Adhan calculation for the supplied local Gregorian date.</summary>
    private static Batoulapps.Adhan.PrayerTimes CalculateAdhan(
        DateOnly date,
        Coordinates coordinates,
        CalculationParameters parameters) =>
        new(coordinates, new DateComponents(date.Year, date.Month, date.Day), parameters);

    /// <summary>Maps application calculation presets to Adhan parameters.</summary>
    private CalculationParameters CreateParameters(AppCalculationMethod method)
    {
        CalculationParameters parameters;
        if (method.Id.Equals("MWL", StringComparison.OrdinalIgnoreCase))
            parameters = AdhanCalculationMethod.MUSLIM_WORLD_LEAGUE.GetParameters();
        else if (method.Id.Equals("ISNA", StringComparison.OrdinalIgnoreCase))
            parameters = AdhanCalculationMethod.NORTH_AMERICA.GetParameters();
        else if (method.Id.Equals("Egypt", StringComparison.OrdinalIgnoreCase))
            parameters = AdhanCalculationMethod.EGYPTIAN.GetParameters();
        else if (method.Id.Equals("Makkah", StringComparison.OrdinalIgnoreCase))
            parameters = AdhanCalculationMethod.UMM_AL_QURA.GetParameters();
        else if (method.Id.Equals("Karachi", StringComparison.OrdinalIgnoreCase))
            parameters = AdhanCalculationMethod.KARACHI.GetParameters();
        else if (method.Id.Equals("Singapore", StringComparison.OrdinalIgnoreCase))
            parameters = AdhanCalculationMethod.SINGAPORE.GetParameters();
        else if (method.Id.Equals("Dubai", StringComparison.OrdinalIgnoreCase))
            parameters = AdhanCalculationMethod.DUBAI.GetParameters();
        else if (method.Id.Equals("Qatar", StringComparison.OrdinalIgnoreCase))
            parameters = AdhanCalculationMethod.QATAR.GetParameters();
        else if (method.Id.Equals("Kuwait", StringComparison.OrdinalIgnoreCase))
            parameters = AdhanCalculationMethod.KUWAIT.GetParameters();
        else if (method.Id.Equals("Moonsighting", StringComparison.OrdinalIgnoreCase))
            parameters = AdhanCalculationMethod.MOON_SIGHTING_COMMITTEE.GetParameters();
        else
            parameters = CreateCustomParameters(method);

        return parameters;
    }

    /// <summary>Creates Adhan parameters for presets without a built-in Adhan equivalent.</summary>
    private CalculationParameters CreateCustomParameters(AppCalculationMethod method)
    {
        var parameters = AdhanCalculationMethod.OTHER.GetParameters();
        parameters.FajrAngle = method.FajrAngle;
        parameters.IshaAngle = method.IshaAngle ?? 0;
        parameters.IshaInterval = method.IshaMinutes ?? 0;
        return parameters;
    }

    /// <summary>Applies user minute corrections to Adhan's adjustment object.</summary>
    private static void ApplyAdjustments(
        CalculationParameters parameters,
        IReadOnlyDictionary<AppPrayer, int>? adjustments)
    {
        if (adjustments is null)
            return;

        parameters.Adjustments.Fajr += adjustments.GetValueOrDefault(AppPrayer.Fajr);
        parameters.Adjustments.Sunrise += adjustments.GetValueOrDefault(AppPrayer.Sunrise);
        parameters.Adjustments.Dhuhr += adjustments.GetValueOrDefault(AppPrayer.Dhuhr);
        parameters.Adjustments.Asr += adjustments.GetValueOrDefault(AppPrayer.Asr);
        parameters.Adjustments.Maghrib += adjustments.GetValueOrDefault(AppPrayer.Maghrib);
        parameters.Adjustments.Isha += adjustments.GetValueOrDefault(AppPrayer.Isha);
    }

    /// <summary>Maps application high-latitude values to Adhan values.</summary>
    private AdhanHighLatitudeRule MapHighLatitudeRule(AppHighLatitudeRule rule)
    {
        if (rule == AppHighLatitudeRule.MiddleOfNight)
            return AdhanHighLatitudeRule.MIDDLE_OF_THE_NIGHT;
        if (rule == AppHighLatitudeRule.OneSeventh)
            return AdhanHighLatitudeRule.SEVENTH_OF_THE_NIGHT;
        if (rule == AppHighLatitudeRule.None)
            _logger.LogWarning("Adhan requires a high-latitude rule; using twilight-angle for legacy None setting");
        return AdhanHighLatitudeRule.TWILIGHT_ANGLE;
    }

}
