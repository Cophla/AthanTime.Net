using AthanTime.Net.Models;
using AthanTime.Net.Services;

namespace AthanTime.Tests;

/// <summary>Located, dated offline DST checks, including recurring rules, overrides and non-hour savings.</summary>
public sealed class SummerTimeTests(TimeZoneService zones, IPrayerCalculator calculator)
{
    [Theory]
    [InlineData(2026, 3, 5, 29)]
    [InlineData(2027, 3, 4, 28)]
    [InlineData(2028, 2, 3, 27)]
    public void UserRulesSupportFirstThursdayAndLastSundayEvenInFourWeekMonths(int year, int month, int thursday, int sunday)
    {
        Assert.Equal(new DateTime(year, month, thursday, 2, 0, 0), zones.RuleDate(year,
            new() { Month = month, Occurrence = 1, Weekday = DayOfWeek.Thursday }));
        Assert.Equal(new DateTime(year, month, sunday, 3, 15, 0), zones.RuleDate(year,
            new() { Month = month, Occurrence = 5, Weekday = DayOfWeek.Sunday, Hour = 3, Minute = 15 }));
    }

    [Theory]
    [InlineData(1, 6)]
    [InlineData(2, 13)]
    [InlineData(3, 20)]
    [InlineData(4, 27)]
    [InlineData(5, 27)]
    public void EveryWeekOccurrenceResolvesInFebruary2026(int occurrence, int day) =>
        Assert.Equal(day, zones.RuleDate(2026, new() { Month = 2, Occurrence = occurrence, Weekday = DayOfWeek.Friday }).Day);

    [Theory]
    [InlineData(2026, 28)]
    [InlineData(2028, 29)]
    public void AnnualCalendarDayUsesMonthEndWhenTheDateDoesNotExist(int year, int day) =>
        Assert.Equal(day, zones.RuleDate(year, new() { Month = 2, UseWeekday = false, Day = 29 }).Day);

    [Fact]
    public void Custom2026LondonSeasonUsesPreChangeClocksAndExactBoundaries()
    {
        var zone = zones.Find("Europe/London");
        var setting = new SummerTimeSettings { Automatic = false, UseCustomSchedule = true };
        var start = new DateTimeOffset(2026, 3, 29, 2, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 10, 25, 2, 0, 0, TimeSpan.Zero);
        Assert.Equal(0, zones.EffectiveSavingsAt(start.AddTicks(-1), zone, setting));
        Assert.Equal(60, zones.EffectiveSavingsAt(start, zone, setting));
        Assert.Equal(60, zones.EffectiveSavingsAt(end.AddTicks(-1), zone, setting));
        Assert.Equal(0, zones.EffectiveSavingsAt(end, zone, setting));
        Assert.Equal(new DateTime(2026, 3, 29, 3, 0, 0), zones.ToLocal(start, zone, setting));
        Assert.Equal(new DateTime(2026, 10, 25, 2, 0, 0), zones.ToLocal(end, zone, setting));
        Assert.Equal(3, zones.CustomTransitions(2026, setting)[0].After.Hour);
        Assert.Equal(2, zones.CustomTransitions(2026, setting)[1].After.Hour);
        // Automatic ignores the user's dates and offset, including in the IANA transition's preceding hour.
        setting.Automatic = true;
        Assert.Equal(new DateTime(2026, 3, 29, 2, 30, 0), zones.ToLocal(start.AddMinutes(-30), zone, setting));
    }

    [Fact]
    public void CustomSydneySeasonCrossesNewYearAndSupportsHalfAnHour()
    {
        var zone = zones.Find("Australia/Sydney");
        var setting = new SummerTimeSettings { Automatic = false, UseCustomSchedule = true, AdjustmentMinutes = 30,
            Start = new() { Month = 10, Occurrence = 1 }, End = new() { Month = 4, Occurrence = 1, Hour = 3 } };
        Assert.Equal(30, zones.EffectiveSavingsAtNoon(new(2026, 1, 15), zone, setting));
        Assert.Equal(0, zones.EffectiveSavingsAtNoon(new(2026, 7, 15), zone, setting));
        Assert.Equal(30, zones.EffectiveSavingsAtNoon(new(2027, 1, 15), zone, setting));
        var january = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTime(2026, 1, 15, 10, 30, 0), zones.ToLocal(january, zone, setting));
        Assert.Equal(january, zones.ToUtc(zones.ToLocal(january, zone, setting), zone, setting));
    }

    [Fact]
    public void CustomClockOverlapsAndGapsResolveConsistentlyForPositiveAndNegativeShifts()
    {
        var zone = zones.Find("Etc/UTC");
        var setting = new SummerTimeSettings { Automatic = false, UseCustomSchedule = true };
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.Zero),
            zones.ToUtc(new(2026, 10, 25, 2, 30, 0), zone, setting));
        Assert.Equal(new DateTime(2026, 3, 29, 3, 30, 0), zones.ToLocal(
            zones.ToUtc(new(2026, 3, 29, 2, 30, 0), zone, setting), zone, setting));
        setting.AdjustmentMinutes = -30;
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 1, 45, 0, TimeSpan.Zero),
            zones.ToUtc(new(2026, 3, 29, 1, 45, 0), zone, setting));
        Assert.Equal(new DateTime(2026, 10, 25, 3, 45, 0), zones.ToLocal(
            zones.ToUtc(new(2026, 10, 25, 3, 15, 0), zone, setting), zone, setting));
    }

    [Fact]
    public void CustomNegativeShiftAndInvalidSeasonAreExplicitNotAutomaticFallbacks()
    {
        var zone = zones.Find("Etc/UTC");
        var setting = new SummerTimeSettings { Automatic = false, UseCustomSchedule = true, AdjustmentMinutes = -30 };
        var instant = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTime(2026, 7, 15, 11, 30, 0), zones.ToLocal(instant, zone, setting));
        setting.End = setting.Start;
        Assert.False(zones.IsCustomScheduleValid(2026, zone, setting));
        Assert.Equal(0, zones.EffectiveSavingsAt(instant, zone, setting));
        setting.AdjustmentMinutes = 60;
        setting.End = new() { Month = 3, Hour = 2, Minute = 30 };
        Assert.False(zones.IsCustomScheduleValid(2026, zone, setting));
    }

    [Theory]
    [InlineData("Europe/London", 1, 0)]
    [InlineData("Europe/London", 7, 60)]
    [InlineData("Europe/Dublin", 1, 0)]
    [InlineData("Europe/Dublin", 7, 60)]
    [InlineData("Asia/Beirut", 1, 0)]
    [InlineData("Asia/Beirut", 7, 60)]
    [InlineData("Australia/Sydney", 1, 60)]
    [InlineData("Australia/Sydney", 7, 0)]
    [InlineData("Australia/Lord_Howe", 1, 30)]
    [InlineData("Asia/Riyadh", 7, 0)]
    public void SelectedDateUsesTheZonesOwnSeason(string id, int month, int minutes) =>
        Assert.Equal(minutes, zones.SavingsAtNoon(new DateOnly(2026, month, 15), zones.Find(id)));

    [Theory]
    [InlineData(2026, 29, 25)]
    [InlineData(2027, 28, 31)]
    [InlineData(2028, 26, 29)]
    public void LondonRecurrenceCalculatesLastSundaysRatherThanRepeatingFixedDates(int year, int marchDay, int octoberDay)
    {
        var changes = zones.Transitions(year, zones.Find("europe/london"));
        Assert.Equal(2, changes.Count);
        Assert.Equal(new DateTime(year, 3, marchDay, 1, 0, 0), changes[0].Before);
        Assert.Equal(new DateTime(year, 3, marchDay, 2, 0, 0), changes[0].After);
        Assert.Equal(new DateTime(year, 10, octoberDay, 2, 0, 0), changes[1].Before);
        Assert.Equal(new DateTime(year, 10, octoberDay, 1, 0, 0), changes[1].After);
    }

    [Fact]
    public void ActualTransitionInstantAndManualChoicesAreDifferentAndNeverDoubleApplied()
    {
        var zone = zones.Find("Europe/London");
        var before = new DateTimeOffset(2026, 3, 29, 0, 59, 59, TimeSpan.Zero);
        Assert.Equal(0, zones.SavingsAt(before, zone));
        Assert.Equal(60, zones.SavingsAt(before.AddSeconds(1), zone));
        Assert.Equal(new DateTime(2026, 3, 29, 2, 0, 0), zones.ToLocal(before.AddSeconds(1), zone));
        Assert.Equal(new DateTime(2026, 3, 29, 1, 0, 0), zones.ToLocal(before.AddSeconds(1), zone,
            new() { Automatic = false, Enabled = false }));
        Assert.Equal(new DateTime(2026, 3, 29, 1, 30, 0), zones.ToLocal(before.AddSeconds(1), zone,
            new() { Automatic = false, Enabled = true, AdjustmentMinutes = 30 }));
        Assert.Equal(new DateTime(2026, 3, 29, 2, 0, 0), zones.ToLocal(before.AddSeconds(1), zone,
            new() { Automatic = true, Enabled = true, AdjustmentMinutes = 180 }));
    }

    [Fact]
    public void ManualChoiceChangesCivilTimesButNotAstronomyOrPrayerInstants()
    {
        var date = new DateOnly(2026, 7, 15);
        var london = new LocationInfo("london", "London", "GB", 51.5074, -0.1278, "Europe/London");
        DailyPrayerTimes Calculate(SummerTimeSettings? setting) => calculator.Calculate(date, london,
            CalculationMethod.Find("MWL"), AsrJuristic.Standard, HighLatitudeRule.AngleBased, summerTime: setting);
        var automatic = Calculate(null);
        var disabled = Calculate(new() { Automatic = false });
        var manual = Calculate(new() { Automatic = false, Enabled = true, AdjustmentMinutes = 30 });
        for (var index = 0; index < automatic.Times.Count; index++)
        {
            Assert.Equal(automatic.Times[index].UtcTime, disabled.Times[index].UtcTime);
            Assert.Equal(TimeSpan.FromHours(1), automatic.Times[index].LocalTime - disabled.Times[index].LocalTime);
            Assert.Equal(TimeSpan.FromMinutes(30), manual.Times[index].LocalTime - disabled.Times[index].LocalTime);
        }
        Assert.Equal(automatic.QiblaBearing, manual.QiblaBearing);
        Assert.Equal(TimeSpan.FromHours(1), automatic.Midnight - disabled.Midnight);
    }

    [Fact]
    public void HalfHourAndRamadanClockChangesAreAllReportedOffline()
    {
        var halfHour = zones.Transitions(2026, zones.Find("Australia/Lord_Howe"));
        Assert.Equal(2, halfHour.Count);
        Assert.All(halfHour, change => Assert.Equal(30, Math.Abs(change.SavingsAfterMinutes - change.SavingsBeforeMinutes)));
        Assert.Equal(2, zones.Transitions(2026, zones.Find("Africa/Casablanca")).Count);
        Assert.Empty(zones.Transitions(2026, zones.Find("Asia/Riyadh")));
    }

    [Fact]
    public void NegativeIanaWinterNotationHasAnIntuitiveSummerCheckboxWithoutChangingTheClock()
    {
        var zone = zones.Find("Europe/Dublin");
        var winter = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var summer = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTime(2026, 1, 15, 12, 0, 0), zones.ToLocal(winter, zone));
        Assert.Equal(new DateTime(2026, 7, 15, 13, 0, 0), zones.ToLocal(summer, zone));
        Assert.Equal(new DateTime(2026, 7, 15, 12, 0, 0), zones.ToLocal(summer, zone, new() { Automatic = false }));
        var transitions = zones.Transitions(2026, zone);
        Assert.Equal(0, transitions[0].SavingsBeforeMinutes);
        Assert.Equal(60, transitions[0].SavingsAfterMinutes);
    }

    [Fact]
    public void London2026IslamicMidnightUsesTheElapsedNightAcrossTheSpringChange()
    {
        var location = new LocationInfo("london", "London", "GB", 51.5074, -0.1278, "Europe/London");
        DailyPrayerTimes Day(DateOnly date) => calculator.Calculate(date, location,
            CalculationMethod.Find("MWL"), AsrJuristic.Standard, HighLatitudeRule.AngleBased);
        var day = Day(new DateOnly(2026, 3, 28));
        var start = day.Times.Single(prayer => prayer.Prayer == Prayer.Maghrib).UtcTime!.Value;
        var end = Day(new DateOnly(2026, 3, 29)).Times.Single(prayer => prayer.Prayer == Prayer.Sunrise).UtcTime!.Value;
        var expected = zones.ToLocal(start.AddTicks((end - start).Ticks / 2), zones.Find(location.TimeZoneId));
        Assert.Equal(expected, day.Midnight);
    }

    [Fact]
    public void EveryBundledCityHasAnOfflineZoneRatherThanFallingBackToUtc()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(AppContext.BaseDirectory, "Data", "locations.db"),
            Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly, Pooling = false
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT timeZoneId FROM locations";
        using var reader = command.ExecuteReader();
        while (reader.Read()) Assert.Contains(reader.GetString(0), zones.Ids, StringComparer.OrdinalIgnoreCase);
    }
}
