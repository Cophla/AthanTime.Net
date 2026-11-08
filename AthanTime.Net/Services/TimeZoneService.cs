using AthanTime.Net.Models;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NodaTime;

namespace AthanTime.Net.Services;

/// <summary>One offline IANA rule source for clocks, prayer conversion and daylight-saving information.</summary>
public sealed class TimeZoneService(IDateTimeZoneProvider provider, ILogger<TimeZoneService> logger)
{
    private readonly IDateTimeZoneProvider _provider = provider;
    private readonly ILogger<TimeZoneService> _logger = logger;
    private readonly Dictionary<string, string> _ids = provider.Ids.ToDictionary(id => id, StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<(string Id, int Year), Offset> _seasonalBaselines = new();

    private Offset SeasonalBaseline(DateTimeZone zone, int year)
    {
        var key = (zone.Id, year);
        if (_seasonalBaselines.TryGetValue(key, out var cached)) return cached;
        if (_seasonalBaselines.Count > 64) _seasonalBaselines.Clear();
        var start = zone.AtLeniently(new LocalDateTime(year, 1, 1, 0, 0)).ToInstant();
        var end = zone.AtLeniently(new LocalDateTime(year + 1, 1, 1, 0, 0)).ToInstant();
        // IANA represents Ireland's winter (and Morocco's Ramadan) as negative savings.
        // Normalize the UI/manual baseline to the lower seasonal contribution; automatic wall offsets never change.
        var baseline = zone.GetZoneIntervals(start, end).Select(interval => interval.Savings).Append(Offset.Zero).Min();
        return _seasonalBaselines.GetOrAdd(key, baseline);
    }

    /// <summary>Bundled database version; future government changes require an application data update.</summary>
    public string Version => _provider.VersionId;
    /// <summary>Offline identifiers, including aliases, usable on every supported operating system.</summary>
    public IReadOnlyList<string> Ids { get; } = provider.Ids.Order(StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>Resolves an identifier case-insensitively; invalid legacy IDs fall back visibly to UTC.</summary>
    public DateTimeZone Find(string id)
    {
        if (_ids.TryGetValue(id, out var canonical)) return _provider[canonical];
        _logger.LogWarning("Time zone {TimeZoneId} is missing from {Version}; UTC is used", id, Version);
        return DateTimeZone.Utc;
    }

    /// <summary>Uses the rule at the actual instant, preserving standard-offset changes and non-hour savings.</summary>
    public DateTime ToLocal(DateTimeOffset utc, DateTimeZone zone, SummerTimeSettings? settings = null)
    {
        var instant = Instant.FromDateTimeOffset(utc);
        var interval = zone.GetZoneInterval(instant);
        var offset = settings is null || settings.Automatic ? interval.WallOffset
            : interval.StandardOffset + SeasonalBaseline(zone, instant.InZone(zone).Year)
                + Offset.FromSeconds(ManualMinutes(instant, zone, settings) * 60);
        return instant.WithOffset(offset).LocalDateTime.ToDateTimeUnspecified();
    }

    /// <summary>Maps a local test/manual value to UTC; real calculated prayers retain their original UTC instant.</summary>
    public DateTimeOffset ToUtc(DateTime local, DateTimeZone zone, SummerTimeSettings? settings = null)
    {
        var value = LocalDateTime.FromDateTime(DateTime.SpecifyKind(local, DateTimeKind.Unspecified));
        var automatic = zone.AtLeniently(value);
        if (settings is null || settings.Automatic) return automatic.ToDateTimeOffset().ToUniversalTime();
        var baseline = automatic.GetZoneInterval().StandardOffset + SeasonalBaseline(zone, value.Year);
        var standardInstant = value.WithOffset(baseline).ToInstant();
        var shiftedInstant = value.WithOffset(baseline + Offset.FromSeconds(settings.AdjustmentMinutes * 60)).ToInstant();
        // Match AtLeniently: choose the earlier valid instant for overlaps, move forward for gaps.
        var standardValid = ManualMinutes(standardInstant, zone, settings) == 0;
        var shiftedValid = ManualMinutes(shiftedInstant, zone, settings) == settings.AdjustmentMinutes;
        var resolved = standardValid && shiftedValid ? Instant.Min(standardInstant, shiftedInstant)
            : shiftedValid ? shiftedInstant : standardValid ? standardInstant : Instant.Max(standardInstant, shiftedInstant);
        return resolved.ToDateTimeOffset();
    }

    /// <summary>Returns the normalized seasonal contribution at noon on the selected civil date.</summary>
    public int SavingsAtNoon(DateOnly date, DateTimeZone zone) => (int)(zone.AtLeniently(
        new LocalDateTime(date.Year, date.Month, date.Day, 12, 0)).GetZoneInterval().Savings
        - SeasonalBaseline(zone, date.Year)).ToTimeSpan().TotalMinutes;

    /// <summary>Returns the seasonal contribution at the actual current instant.</summary>
    public int SavingsAt(DateTimeOffset utc, DateTimeZone zone)
    {
        var instant = Instant.FromDateTimeOffset(utc);
        return (int)(zone.GetZoneInterval(instant).Savings - SeasonalBaseline(zone, instant.InZone(zone).Year))
            .ToTimeSpan().TotalMinutes;
    }

    /// <summary>Reports the chosen automatic, custom-recurring or forced seasonal contribution at an instant.</summary>
    public int EffectiveSavingsAt(DateTimeOffset utc, DateTimeZone zone, SummerTimeSettings settings) =>
        settings.Automatic ? SavingsAt(utc, zone) : ManualMinutes(Instant.FromDateTimeOffset(utc), zone, settings);

    /// <summary>Reports the selected mode at noon; custom schedules use their own civil clock.</summary>
    public int EffectiveSavingsAtNoon(DateOnly date, DateTimeZone zone, SummerTimeSettings settings) =>
        settings.Automatic ? SavingsAtNoon(date, zone) : EffectiveSavingsAt(
            ToUtc(date.ToDateTime(new TimeOnly(12, 0)), zone, settings), zone, settings);

    /// <summary>Evaluates an annual rule with Noda Time's weekday adjusters; unavailable calendar days use month end.</summary>
    public DateTime RuleDate(int year, SummerTimeRule rule)
    {
        var first = new LocalDate(year, rule.Month, 1);
        var weekday = rule.Weekday == DayOfWeek.Sunday ? IsoDayOfWeek.Sunday : (IsoDayOfWeek)rule.Weekday;
        var date = !rule.UseWeekday ? new LocalDate(year, rule.Month, Math.Min(rule.Day,
                CalendarSystem.Iso.GetDaysInMonth(year, rule.Month)))
            : rule.Occurrence == 5 ? first.With(DateAdjusters.EndOfMonth).With(DateAdjusters.PreviousOrSame(weekday))
            : first.With(DateAdjusters.NextOrSame(weekday)).PlusDays((rule.Occurrence - 1) * 7);
        return date.At(new LocalTime(rule.Hour, rule.Minute)).ToDateTimeUnspecified();
    }

    // Validate while constructing the period so callers do not calculate the same transitions twice.
    private (Instant Start, Instant End)? CustomPeriod(int year, DateTimeZone zone, SummerTimeSettings settings)
    {
        var start = RuleDate(year, settings.Start);
        var end = RuleDate(year, settings.End);
        if (start == end) return null;
        if (end < start) end = RuleDate(year + 1, settings.End); // Southern seasons span New Year.
        var startInstant = CustomInstant(start, zone, 0);
        var endInstant = CustomInstant(end, zone, settings.AdjustmentMinutes);
        return endInstant > startInstant ? (startInstant, endInstant) : null;
    }

    private Instant CustomInstant(DateTime clock, DateTimeZone zone, int minutesBefore)
    {
        var local = LocalDateTime.FromDateTime(clock);
        var offset = zone.AtLeniently(local).GetZoneInterval().StandardOffset + SeasonalBaseline(zone, clock.Year)
            + Offset.FromSeconds(minutesBefore * 60);
        return local.WithOffset(offset).ToInstant();
    }

    /// <summary>Rejects an empty/reversed season after applying the before-change clock offsets.</summary>
    public bool IsCustomScheduleValid(int year, DateTimeZone zone, SummerTimeSettings settings) =>
        CustomPeriod(year, zone, settings) is not null;

    private int ManualMinutes(Instant instant, DateTimeZone zone, SummerTimeSettings settings)
    {
        if (!settings.UseCustomSchedule) return settings.Enabled ? settings.AdjustmentMinutes : 0;
        var year = instant.InZone(zone).Year;
        for (var seasonYear = year - 1; seasonYear <= year; seasonYear++)
        {
            if (CustomPeriod(seasonYear, zone, settings) is { } period
                && instant >= period.Start && instant < period.End) return settings.AdjustmentMinutes;
        }
        return 0;
    }

    /// <summary>Previews both user-defined annual changes, including their before/after local clocks.</summary>
    public IReadOnlyList<SummerTimeTransition> CustomTransitions(int year, SummerTimeSettings settings) =>
        new[]
        {
            new SummerTimeTransition(RuleDate(year, settings.Start),
                RuleDate(year, settings.Start).AddMinutes(settings.AdjustmentMinutes), 0, settings.AdjustmentMinutes),
            new SummerTimeTransition(RuleDate(year, settings.End),
                RuleDate(year, settings.End).AddMinutes(-settings.AdjustmentMinutes), settings.AdjustmentMinutes, 0)
        }.OrderBy(change => change.Before).ToArray();

    /// <summary>Computes every seasonal change for the local year from recurring and historical database rules.</summary>
    public IReadOnlyList<SummerTimeTransition> Transitions(int year, DateTimeZone zone)
    {
        var start = Instant.FromUtc(year - 1, 12, 30, 0, 0);
        var end = Instant.FromUtc(year + 1, 1, 3, 0, 0);
        var result = new List<SummerTimeTransition>();
        var baseline = SeasonalBaseline(zone, year);
        foreach (var interval in zone.GetZoneIntervals(start, end))
        {
            if (!interval.HasStart || interval.Start < start) continue;
            var before = zone.GetZoneInterval(interval.Start - Duration.FromTicks(1));
            if (before.Savings == interval.Savings) continue;
            var localBefore = interval.Start.WithOffset(before.WallOffset).LocalDateTime;
            if (localBefore.Year != year) continue;
            result.Add(new(localBefore.ToDateTimeUnspecified(),
                interval.Start.WithOffset(interval.WallOffset).LocalDateTime.ToDateTimeUnspecified(),
                (int)(before.Savings - baseline).ToTimeSpan().TotalMinutes,
                (int)(interval.Savings - baseline).ToTimeSpan().TotalMinutes));
        }
        return result;
    }
}

/// <summary>A seasonal transition shown as the wall clock immediately before and after the change.</summary>
public sealed record SummerTimeTransition(DateTime Before, DateTime After, int SavingsBeforeMinutes, int SavingsAfterMinutes);
