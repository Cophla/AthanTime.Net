namespace AthanTime.Net.Models;

/// <summary>Supported schools for calculating the start of Asr.</summary>
public enum AsrJuristic { Standard, Hanafi }

/// <summary>Supported rules for twilight at high latitudes, plus a migrated legacy value.</summary>
public enum HighLatitudeRule { AngleBased, MiddleOfNight, OneSeventh, None }

/// <summary>Describes a user-selectable prayer calculation preset.</summary>
public sealed record CalculationMethod(
    string Id,
    string Name,
    double FajrAngle,
    double? IshaAngle = null,
    int? IshaMinutes = null)
{
    /// <summary>Gets all supported application presets.</summary>
    public static IReadOnlyList<CalculationMethod> All { get; } =
    [
        new("MWL", "Muslim World League", 18, 17),
        new("ISNA", "ISNA (North America)", 15, 15),
        new("Egypt", "Egyptian General Authority", 19.5, 17.5),
        new("Makkah", "Umm al-Qura, Makkah", 18, IshaMinutes: 90),
        new("Karachi", "University of Islamic Sciences, Karachi", 18, 18),
        new("Singapore", "MUIS, Singapore", 20, 18),
        new("Turkey", "Diyanet, Türkiye", 18, 17),
        new("Dubai", "Dubai", 18.2, 18.2),
        new("Qatar", "Qatar", 18, IshaMinutes: 90),
        new("Kuwait", "Kuwait", 18, 17.5),
        new("Moonsighting", "Moonsighting Committee", 18, 18)
    ];

    /// <summary>Finds a preset identifier using an ordinal case-insensitive comparison.</summary>
    public static CalculationMethod Find(string id) =>
        All.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? All[0];
}

/// <summary>Stable schedule identifiers for the five prayers and the Shoroq/sunrise time marker.</summary>
public enum Prayer { Fajr, Sunrise, Dhuhr, Asr, Maghrib, Isha }

/// <summary>Associates a scheduled prayer or sunrise with local civil time and its original UTC instant when available.</summary>
public sealed record PrayerTime(Prayer Prayer, DateTime LocalTime, DateTimeOffset? UtcTime = null);

/// <summary>Contains five prayer times, sunrise, Islamic midnight and qibla for one local civil date.</summary>
public sealed record DailyPrayerTimes(
    DateOnly Date,
    IReadOnlyList<PrayerTime> Times,
    DateTime Midnight,
    double QiblaBearing);

/// <summary>Describes a regional calculation convention without altering prayer times.</summary>
public sealed record PrayerConvention(
    string Id,
    string MethodId,
    AsrJuristic AsrJuristic,
    HighLatitudeRule HighLatitudeRule,
    string Reference);

/// <summary>JSON model for one convention assigned to countries and optional cities.</summary>
public sealed class PrayerConventionRule
{
    public string Id { get; set; } = "default";
    public string MethodId { get; set; } = "MWL";
    public AsrJuristic AsrJuristic { get; set; } = AsrJuristic.Standard;
    public HighLatitudeRule HighLatitudeRule { get; set; } = HighLatitudeRule.AngleBased;
    public string Reference { get; set; } = "Regional convention; verify with the local authority.";
    public string[] CountryCodes { get; set; } = [];
    public string[] LocationIds { get; set; } = [];
}

/// <summary>Root model for bundled prayer-convention JSON.</summary>
public sealed class PrayerConventionData
{
    public int SchemaVersion { get; set; } = 1;
    public string Updated { get; set; } = "";
    public string Notice { get; set; } = "";
    public PrayerConventionRule Default { get; set; } = new();
    public PrayerConventionRule[] Rules { get; set; } = [];
}
