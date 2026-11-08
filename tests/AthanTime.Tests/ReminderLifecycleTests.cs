using System.Globalization;
using AthanTime.Net.Models;
using AthanTime.Net.ViewModels;

namespace AthanTime.Tests;

/// <summary>Exercises reminders with injected clocks, in-memory profiles and fake audio; no native devices are opened.</summary>
[Collection("UI")]
public sealed class ReminderLifecycleTests(UiTestHost ui, ViewModelHarness harness)
{
    /// <summary>Each switch suppresses only its own event, at the separately configured UTC offset.</summary>
    [Theory]
    [InlineData(Prayer.Dhuhr, false, false, false, 30, 10)]
    [InlineData(Prayer.Dhuhr, false, false, true, 30, 10)]
    [InlineData(Prayer.Dhuhr, false, true, false, 30, 10)]
    [InlineData(Prayer.Dhuhr, false, true, true, 30, 10)]
    [InlineData(Prayer.Dhuhr, true, false, false, 30, 10)]
    [InlineData(Prayer.Dhuhr, true, false, true, 30, 10)]
    [InlineData(Prayer.Dhuhr, true, true, false, 30, 10)]
    [InlineData(Prayer.Dhuhr, true, true, true, 30, 10)]
    [InlineData(Prayer.Dhuhr, true, true, true, 15, 5)]
    [InlineData(Prayer.Sunrise, false, false, false, 30, 10)]
    [InlineData(Prayer.Sunrise, false, false, true, 30, 10)]
    [InlineData(Prayer.Sunrise, false, true, false, 30, 10)]
    [InlineData(Prayer.Sunrise, false, true, true, 30, 10)]
    [InlineData(Prayer.Sunrise, true, false, false, 30, 10)]
    [InlineData(Prayer.Sunrise, true, false, true, 30, 10)]
    [InlineData(Prayer.Sunrise, true, true, false, 30, 10)]
    [InlineData(Prayer.Sunrise, true, true, true, 30, 10)]
    [InlineData(Prayer.Sunrise, true, true, true, 15, 5)]
    public Task ReminderMomentsHaveIndependentSwitchesAndTiming(Prayer prayer, bool before, bool atTime, bool after, int minutesBefore, int minutesAfter) => ui.Run(() =>
    {
        ConfigurePrayer(prayer);
        var prayerUtc = new DateTimeOffset(2026, 9, 26, prayer == Prayer.Sunrise ? 6 : 12, 0, 0, TimeSpan.Zero);
        harness.Clock.Now = prayerUtc.AddMinutes(-minutesBefore).AddSeconds(-1);
        using var vm = harness.Create();
        var row = vm.NotificationRows.Single(item => item.Prayer == prayer);
        row.MinutesBefore = minutesBefore; row.MinutesAfter = minutesAfter;
        row.NotifyBefore = before; row.NotifyAtTime = atTime; row.NotifyAfter = after;
        var alerts = new List<PrayerAlert>();
        vm.PrayerDue += (_, alert) => alerts.Add(alert);
        vm.Tick();
        Assert.Empty(alerts);
        AdvanceSeconds(vm, 1); vm.Tick();
        Assert.Equal(before ? 1 : 0, alerts.Count);
        Assert.Empty(harness.Audio.Played);
        AdvanceSeconds(vm, minutesBefore * 60 - 1);
        Assert.Equal(before ? 1 : 0, alerts.Count);
        AdvanceSeconds(vm, 1); vm.Tick();
        Assert.Equal((before ? 1 : 0) + (atTime ? 1 : 0), alerts.Count);
        Assert.Equal(atTime ? 1 : 0, harness.Audio.Played.Count);
        AdvanceSeconds(vm, minutesAfter * 60 - 1);
        Assert.DoesNotContain(alerts, alert => alert.Moment == PrayerAlertMoment.After);
        AdvanceSeconds(vm, 1); vm.Tick();
        var expected = new List<PrayerAlertMoment>();
        if (before) expected.Add(PrayerAlertMoment.Before);
        if (atTime) expected.Add(PrayerAlertMoment.AtTime);
        if (after) expected.Add(PrayerAlertMoment.After);
        Assert.Equal(expected, alerts.Select(alert => alert.Moment));
        Assert.Equal(expected.Count, harness.Settings.Value.RecentPrayerAlerts.Count);
        Assert.Equal(atTime ? 1 : 0, harness.Audio.Played.Count);
    });

    /// <summary>The configured advance popup fires exactly once without starting the Athan early.</summary>
    [Theory]
    [InlineData(15, true)]
    [InlineData(30, true)]
    [InlineData(30, false)]
    public Task BeforePrayerUsesConfiguredMinutesAndRespectsItsSwitch(int minutes, bool enabled) => ui.Run(() =>
    {
        ConfigurePrayer(Prayer.Dhuhr);
        var prayerUtc = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
        harness.Clock.Now = prayerUtc.AddMinutes(-minutes).AddSeconds(-1);
        using var vm = harness.Create();
        var row = vm.NotificationRows.Single(item => item.Prayer == Prayer.Dhuhr);
        row.MinutesBefore = minutes;
        row.NotifyBefore = enabled;
        var alerts = new List<PrayerAlert>();
        vm.PrayerDue += (_, alert) => alerts.Add(alert);
        vm.Tick();
        Assert.Empty(alerts);
        Assert.Empty(harness.Audio.Played);
        AdvanceSeconds(vm, 1);
        vm.Tick();
        if (enabled)
        {
            var before = Assert.Single(alerts);
            Assert.Equal(PrayerAlertMoment.Before, before.Moment);
            Assert.Equal(string.Format(CultureInfo.InvariantCulture, vm["BeforePrayerMessage"], vm["Dhuhr"], minutes), before.Message);
        }
        else Assert.Empty(alerts);
        Assert.Empty(harness.Audio.Played);
        AdvanceSeconds(vm, minutes * 60);
        vm.Tick();
        Assert.Single(alerts, alert => alert.Moment == PrayerAlertMoment.AtTime);
        Assert.Equal(enabled ? 2 : 1, alerts.Count);
        Assert.Equal(["prayer.ogg"], harness.Audio.Played);
    });

    /// <summary>A delivered UTC identity survives construction of a new screen within the same minute.</summary>
    [Theory]
    [InlineData(Prayer.Dhuhr, PrayerAlertMoment.Before, -15)]
    [InlineData(Prayer.Dhuhr, PrayerAlertMoment.AtTime, 0)]
    [InlineData(Prayer.Dhuhr, PrayerAlertMoment.After, 5)]
    [InlineData(Prayer.Sunrise, PrayerAlertMoment.Before, -15)]
    [InlineData(Prayer.Sunrise, PrayerAlertMoment.AtTime, 0)]
    [InlineData(Prayer.Sunrise, PrayerAlertMoment.After, 5)]
    public Task RestartWithinReminderMinuteDoesNotRepeatAudioOrNotification(Prayer prayer, PrayerAlertMoment moment, int offsetMinutes) => ui.Run(() =>
    {
        ConfigurePrayer(prayer);
        var notification = harness.Settings.Value.PrayerNotifications[prayer];
        notification.NotifyBefore = true;
        notification.NotifyAfter = true;
        notification.MinutesBefore = 15;
        notification.MinutesAfter = 5;
        var eventUtc = new DateTimeOffset(2026, 9, 26, prayer == Prayer.Sunrise ? 6 : 12, 0, 0, TimeSpan.Zero).AddMinutes(offsetMinutes);
        harness.Clock.Now = eventUtc.AddSeconds(15);
        using (var first = harness.Create())
        {
            var delivered = new List<PrayerAlert>();
            var savesBeforeDispatch = harness.Settings.Saves;
            first.PrayerDue += (_, alert) =>
            {
                // Save the UTC identity before a popup subscriber can observe the event.
                Assert.True(harness.Settings.Saves > savesBeforeDispatch);
                Assert.Equal($"manual-test:{prayer}:{moment}:{eventUtc.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture)}",
                    Assert.Single(harness.Settings.Value.RecentPrayerAlerts));
                delivered.Add(alert);
            };
            first.Tick(); first.Tick();
            Assert.Equal(moment, Assert.Single(delivered).Moment);
        }
        harness.Audio.IsBusy = false;
        harness.Clock.Now = harness.Clock.Now.AddSeconds(10);
        using var restarted = harness.Create();
        var repeated = 0;
        restarted.PrayerDue += (_, _) => repeated++;
        restarted.Tick(); restarted.Tick();
        Assert.Equal(0, repeated);
        Assert.Equal(moment == PrayerAlertMoment.AtTime ? 1 : 0, harness.Audio.Played.Count);
        Assert.Single(harness.Settings.Value.RecentPrayerAlerts);
    });

    /// <summary>Backward clock corrections cannot replay an already delivered UTC minute.</summary>
    [Fact]
    public Task ClockReversalDoesNotRepeatDeliveredPrayer() => ui.Run(() =>
    {
        ConfigurePrayer(Prayer.Dhuhr);
        harness.Clock.Now = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
        using var vm = harness.Create();
        var alerts = new List<PrayerAlert>();
        vm.PrayerDue += (_, alert) => alerts.Add(alert);
        vm.Tick();
        harness.Clock.Now = harness.Clock.Now.AddHours(1);
        vm.Tick();
        harness.Clock.Now = harness.Clock.Now.AddHours(-1);
        vm.Tick(); vm.Tick();
        Assert.Single(alerts);
        Assert.Single(harness.Audio.Played);
    });

    /// <summary>UTC events with nonzero seconds never fire early just because their displayed minute has started.</summary>
    [Fact]
    public Task PrayerDoesNotFireBeforeItsExactUtcInstant() => ui.Run(() =>
    {
        ConfigurePrayer(Prayer.Fajr);
        harness.Calculator.Fajr = new TimeOnly(12, 0, 30);
        harness.Clock.Now = new DateTimeOffset(2026, 9, 26, 12, 0, 29, TimeSpan.Zero);
        using var vm = harness.Create();
        var delivered = 0;
        vm.PrayerDue += (_, _) => delivered++;
        vm.Tick();
        Assert.Equal(0, delivered);
        AdvanceSeconds(vm, 1);
        Assert.Equal(1, delivered);
        Assert.Single(harness.Audio.Played);
    });

    /// <summary>Persisted reminder identity comparisons follow the application's ordinal-ignore-case policy.</summary>
    [Fact]
    public Task RestoredReminderIdentityIgnoresCase() => ui.Run(() =>
    {
        ConfigurePrayer(Prayer.Dhuhr);
        harness.Settings.Value.RecentPrayerAlerts = ["MANUAL-TEST:DHUHR:ATTIME:202609261200"];
        harness.Clock.Now = new DateTimeOffset(2026, 9, 26, 12, 0, 15, TimeSpan.Zero);
        using var vm = harness.Create();
        vm.Tick();
        Assert.Empty(harness.Audio.Played);
        Assert.Single(harness.Settings.Value.RecentPrayerAlerts);
    });

    /// <summary>Reminder identities use Gregorian invariant UTC even when the operating-system culture uses another calendar.</summary>
    [Fact]
    public Task ReminderIdentityDoesNotDependOnSystemCalendar() => ui.Run(() =>
    {
        ConfigurePrayer(Prayer.Dhuhr);
        harness.Clock.Now = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            using var vm = harness.Create();
            vm.Tick();
            Assert.Equal("manual-test:Dhuhr:AtTime:202609261200", Assert.Single(harness.Settings.Value.RecentPrayerAlerts));
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
    });

    /// <summary>Resuming exactly at a due instant is allowed; a missed instant in that minute is not caught up.</summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(20, 0)]
    public Task TimerGapSkipsMissedPrayerButAllowsAnEventDueAtResume(int resumedSecond, int expectedAlerts) => ui.Run(() =>
    {
        ConfigurePrayer(Prayer.Dhuhr);
        harness.Clock.Now = new DateTimeOffset(2026, 9, 26, 11, 59, 40, TimeSpan.Zero);
        using var vm = harness.Create();
        var alerts = 0;
        vm.PrayerDue += (_, _) => alerts++;
        vm.Tick();
        harness.Clock.Now = new DateTimeOffset(2026, 9, 26, 12, 0, resumedSecond, TimeSpan.Zero);
        vm.Tick();
        harness.Clock.Now = harness.Clock.Now.AddSeconds(1);
        vm.Tick();
        Assert.Equal(expectedAlerts, alerts);
        Assert.Equal(expectedAlerts, harness.Audio.Played.Count);
        Assert.Equal(expectedAlerts, harness.Settings.Value.RecentPrayerAlerts.Count);
    });

    /// <summary>Large forward/backward jumps close readings and start full, independent Adhkar intervals.</summary>
    [Theory]
    [InlineData(10)]
    [InlineData(-10)]
    public Task ClockGapRestartsAudioAndTextIntervalsWithoutCatchUp(int clockShiftMinutes) => ui.Run(() =>
    {
        DisablePrayerAlerts();
        harness.Settings.Value.Adhkar = new() { Enabled = true, IntervalMinutes = 1, AudioFiles = ["interval.ogg"] };
        harness.Settings.Value.TextAdhkar = new() { Enabled = true, PauseMinutes = 1, DisplaySeconds = 120, Texts = ["First", "Second"] };
        using var vm = harness.Create();
        string? visible = null;
        vm.TextAdhkarChanged += (_, text) => visible = text;
        vm.Tick();
        AdvanceSeconds(vm, 60);
        Assert.Equal("First", visible);
        Assert.Single(harness.Audio.Played);
        harness.Audio.IsBusy = false;
        harness.Clock.Now = harness.Clock.Now.AddMinutes(clockShiftMinutes);
        vm.Tick(); vm.Tick();
        Assert.Null(visible);
        Assert.Single(harness.Audio.Played);
        AdvanceSeconds(vm, 59);
        Assert.Null(visible);
        Assert.Single(harness.Audio.Played);
        AdvanceSeconds(vm, 1);
        Assert.Equal("First", visible);
        Assert.Equal(2, harness.Audio.Played.Count);
    });

    /// <summary>Both automatic expiry and manual resume discard the prayer suppressed during a pause.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ResumingPauseDoesNotReleaseSuppressedPrayerWithinItsMinute(bool manually) => ui.Run(() =>
    {
        ConfigurePrayer(Prayer.Dhuhr);
        harness.Settings.Value.PrayerNotifications[Prayer.Dhuhr].NotifyAfter = true;
        harness.Settings.Value.PrayerNotifications[Prayer.Dhuhr].MinutesAfter = 1;
        harness.Clock.Now = new DateTimeOffset(2026, 9, 26, 11, 59, 20, TimeSpan.Zero);
        using var vm = harness.Create();
        vm.NotificationPauseMinutes = manually ? 2 : 1;
        vm.ToggleNotificationsPause();
        var alerts = new List<PrayerAlert>();
        vm.PrayerDue += (_, alert) => alerts.Add(alert);
        vm.Tick();
        AdvanceSeconds(vm, 60);
        if (manually) vm.ToggleNotificationsPause();
        vm.Tick();
        Assert.False(vm.NotificationsPaused);
        Assert.Empty(alerts);
        Assert.Empty(harness.Audio.Played);
        AdvanceSeconds(vm, 40);
        Assert.Equal(PrayerAlertMoment.After, Assert.Single(alerts).Moment);
        Assert.Empty(harness.Audio.Played);
    });

    /// <summary>Adjacent-day before/after reminders remain deduplicated even after midnight and a backward jump.</summary>
    [Fact]
    public Task MidnightDoesNotClearDeliveredAdjacentDayIdentities() => ui.Run(() =>
    {
        DisablePrayerAlerts();
        harness.Calculator.Fajr = new TimeOnly(0, 5);
        harness.Calculator.Isha = new TimeOnly(23, 55);
        harness.Settings.Value.PrayerNotifications[Prayer.Fajr].NotifyBefore = true;
        harness.Settings.Value.PrayerNotifications[Prayer.Fajr].MinutesBefore = 10;
        harness.Settings.Value.PrayerNotifications[Prayer.Isha].NotifyAfter = true;
        harness.Settings.Value.PrayerNotifications[Prayer.Isha].MinutesAfter = 10;
        harness.Clock.Now = new DateTimeOffset(2026, 9, 26, 23, 55, 0, TimeSpan.Zero);
        using var vm = harness.Create();
        vm.NextDay(); vm.NextDay();
        var selectedDate = vm.DisplayDate;
        var alerts = new List<PrayerAlert>();
        vm.PrayerDue += (_, alert) => alerts.Add(alert);
        vm.Tick();
        harness.Clock.Now = harness.Clock.Now.AddMinutes(10);
        vm.Tick();
        harness.Clock.Now = harness.Clock.Now.AddMinutes(-10);
        vm.Tick();
        Assert.Equal(2, alerts.Count);
        Assert.Contains(alerts, alert => alert.Moment == PrayerAlertMoment.Before && alert.PrayerName == "Fajr");
        Assert.Contains(alerts, alert => alert.Moment == PrayerAlertMoment.After && alert.PrayerName == "Isha");
        Assert.Equal(2, harness.Settings.Value.RecentPrayerAlerts.Count);
        Assert.Equal(selectedDate, vm.DisplayDate);
    });

    /// <summary>London's repeated civil hour remains two distinct UTC hours, never a second Athan for one prayer.</summary>
    [Fact]
    public Task London2026AutumnRepeatedHourDoesNotRepeatPrayer() => ui.Run(() =>
    {
        ConfigurePrayer(Prayer.Fajr);
        var london = new LocationInfo("london-fold", "London", "GB", 51.5074, -0.1278, "Europe/London");
        harness.Settings.Value.SavedLocations = [london];
        harness.Settings.Value.LocationId = london.Id;
        harness.Calculator.Fajr = new TimeOnly(1, 30);
        harness.Clock.Now = new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero);
        using var vm = harness.Create();
        var alerts = 0;
        vm.PrayerDue += (_, _) => alerts++;
        vm.Tick();
        var firstCivilClock = vm.CurrentClock;
        harness.Clock.Now = harness.Clock.Now.AddHours(1);
        vm.Tick();
        Assert.Equal(firstCivilClock, vm.CurrentClock);
        Assert.Equal(1, alerts);
        Assert.Single(harness.Audio.Played);
    });

    /// <summary>The persisted ledger and its in-memory mirror stay bounded rather than growing on every tick.</summary>
    [Fact]
    public Task DeliveredReminderLedgerKeepsOnlyTheLatest128Identities() => ui.Run(() =>
    {
        ConfigurePrayer(Prayer.Dhuhr);
        harness.Settings.Value.RecentPrayerAlerts = Enumerable.Range(0, 128).Select(index => $"old-{index}").ToList();
        harness.Clock.Now = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
        using var vm = harness.Create();
        vm.Tick(); vm.Tick();
        var history = harness.Settings.Value.RecentPrayerAlerts;
        Assert.Equal(128, history.Count);
        Assert.DoesNotContain("old-0", history, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("manual-test:Dhuhr:AtTime:202609261200", history, StringComparer.OrdinalIgnoreCase);
    });

    /// <summary>Notification title, prayer name and message follow each supported language without changing event identity.</summary>
    [Fact]
    public Task ReminderMessagesUseAllEightSelectedLanguages() => ui.Run(() =>
    {
        ConfigurePrayer(Prayer.Dhuhr);
        harness.Clock.Now = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
        using var vm = harness.Create();
        PrayerAlert? delivered = null;
        vm.PrayerDue += (_, alert) => delivered = alert;
        foreach (var language in vm.Languages)
        {
            vm.SelectedLanguage = language;
            delivered = null;
            vm.Tick();
            Assert.NotNull(delivered);
            Assert.Equal(vm["PrayerReminder"], delivered.Title);
            Assert.Equal(vm["Dhuhr"], delivered.PrayerName);
            Assert.Equal(string.Format(vm["AtPrayerMessage"], vm["Dhuhr"]), delivered.Message);
            harness.Clock.Now = harness.Clock.Now.AddDays(1);
        }
        Assert.Equal(8, harness.Settings.Value.RecentPrayerAlerts.Count);
    });

    /// <summary>Sunrise retains all three optional reminders/audio without being described as a prayer.</summary>
    [Theory]
    [InlineData(PrayerAlertMoment.Before, 5, 45, "BeforeSunriseMessage")]
    [InlineData(PrayerAlertMoment.AtTime, 6, 0, "AtSunriseMessage")]
    [InlineData(PrayerAlertMoment.After, 6, 5, "AfterSunriseMessage")]
    public Task SunriseRemindersUseSunriseWordingInEveryLanguage(PrayerAlertMoment moment, int hour, int minute, string messageKey) => ui.Run(() =>
    {
        ConfigurePrayer(Prayer.Sunrise);
        harness.Settings.Value.Use24HourClock = true;
        var settings = harness.Settings.Value.PrayerNotifications[Prayer.Sunrise];
        settings.NotifyBefore = moment == PrayerAlertMoment.Before;
        settings.NotifyAtTime = moment == PrayerAlertMoment.AtTime;
        settings.NotifyAfter = moment == PrayerAlertMoment.After;
        settings.MinutesBefore = 15;
        settings.MinutesAfter = 5;
        harness.Clock.Now = new DateTimeOffset(2026, 9, 26, hour, minute, 0, TimeSpan.Zero);
        using var vm = harness.Create();
        PrayerAlert? delivered = null;
        vm.PrayerDue += (_, alert) => delivered = alert;
        foreach (var language in vm.Languages)
        {
            vm.SelectedLanguage = language;
            delivered = null;
            vm.Tick();
            Assert.NotNull(delivered);
            Assert.Equal(moment, delivered.Moment);
            Assert.Equal(vm["SunriseReminder"], delivered.Title);
            Assert.NotEqual(vm["PrayerReminder"], delivered.Title);
            Assert.Equal(vm["Sunrise"], delivered.PrayerName);
            Assert.Equal(string.Format(vm[messageKey], vm["Sunrise"], moment == PrayerAlertMoment.Before ? 15 : 5), delivered.Message);
            Assert.Equal(new TimeOnly(6, 0).ToString("HH:mm", CultureInfo.GetCultureInfo(language.Code)), delivered.Time);
            harness.Clock.Now = harness.Clock.Now.AddDays(1);
        }
        Assert.Equal(8, harness.Settings.Value.RecentPrayerAlerts.Count);
        Assert.All(harness.Settings.Value.RecentPrayerAlerts, key => Assert.Contains(":Sunrise:", key));
        Assert.Equal(moment == PrayerAlertMoment.AtTime ? 8 : 0, harness.Audio.Played.Count);
    });

    private void ConfigurePrayer(Prayer prayer)
    {
        DisablePrayerAlerts();
        var notification = harness.Settings.Value.PrayerNotifications[prayer];
        notification.NotifyAtTime = true;
        notification.AudioEnabled = true;
        notification.AudioPath = "prayer.ogg";
    }

    private void DisablePrayerAlerts()
    {
        foreach (var notification in harness.Settings.Value.PrayerNotifications.Values)
        {
            notification.NotifyBefore = false;
            notification.NotifyAtTime = false;
            notification.NotifyAfter = false;
        }
    }

    private void AdvanceSeconds(MainViewModel vm, int seconds)
    {
        for (var second = 0; second < seconds; second++)
        {
            harness.Clock.Now = harness.Clock.Now.AddSeconds(1);
            vm.Tick();
        }
    }
}
