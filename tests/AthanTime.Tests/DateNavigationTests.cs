using AthanTime.Net.Models;
using AthanTime.Net.Services;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AthanTime.Tests;

/// <summary>Exercises the real calculator through date navigation across automatic daylight-saving changes.</summary>
[Collection("UI")]
public sealed class DateNavigationTests(UiTestHost ui, ViewModelHarness harness, IPrayerCalculator calculator)
{
    [Fact]
    public Task PcDateJumpRefreshesSidonNextAthanAndRenderedCountdown() => ui.Run(() =>
    {
        var location = new LocationInfo("sidon-clock", "Sidon", "LB", 33.56, 35.37, "Asia/Beirut");
        harness.Clock.Now = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
        harness.Settings.Value.LocationId = location.Id;
        harness.Settings.Value.SavedLocations = [location];
        harness.Settings.Value.Use24HourClock = true;
        using var vm = harness.Create(calculator: calculator);
        var window = new AthanTime.Net.MainWindow { DataContext = vm, ForceClose = true };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            vm.Tick();
            var notifications = new List<string?>();
            vm.PropertyChanged += (_, change) => notifications.Add(change.PropertyName);
            var previousCountdown = vm.Countdown;
            harness.Clock.Now = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
            vm.Tick();
            Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(previousCountdown, vm.Countdown);
            Assert.Contains(nameof(vm.Countdown), notifications);
            var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToArray();
            Assert.Contains(vm.Countdown, texts);
            Assert.Contains(vm.NextPrayerTime, texts);
            Assert.DoesNotContain(previousCountdown, texts);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("Asia/Beirut", 33.56, 35.37, 3, 28, 60)]
    [InlineData("Asia/Beirut", 33.56, 35.37, 10, 24, -60)]
    [InlineData("Europe/London", 51.5074, -0.1278, 3, 28, 60)]
    [InlineData("Europe/London", 51.5074, -0.1278, 10, 24, -60)]
    public Task AutomaticTimesFollowTheBrowsedDateAndReturnFromTheCache(string zone, double latitude,
        double longitude, int month, int day, int offsetChange) => ui.Run(() =>
    {
        var date = new DateOnly(2026, month, day);
        var location = new LocationInfo("dst-test", "DST test", "", latitude, longitude, zone);
        harness.Clock.Now = new DateTimeOffset(2026, month, day, 12, 0, 0, TimeSpan.Zero);
        harness.Settings.Value.LocationId = location.Id;
        harness.Settings.Value.SavedLocations = [location];
        harness.Settings.Value.MethodId = "MWL";
        harness.Settings.Value.Use24HourClock = true;
        using var vm = harness.Create(calculator: calculator);
        var before = vm.PrayerRows.Select(row => row.Time).ToArray();
        vm.NextDay();
        var expected = calculator.Calculate(date.AddDays(1), location, CalculationMethod.Find("MWL"),
            harness.Settings.Value.AsrJuristic, harness.Settings.Value.HighLatitudeRule);
        Assert.Equal(expected.Times.Select(time => time.LocalTime.ToString("HH:mm")), vm.PrayerRows.Select(row => row.Time));
        var standard = calculator.Calculate(date.AddDays(1), location, CalculationMethod.Find("MWL"),
            harness.Settings.Value.AsrJuristic, harness.Settings.Value.HighLatitudeRule,
            summerTime: new() { Automatic = false });
        var nextSavings = offsetChange > 0 ? 60 : 0;
        Assert.All(expected.Times.Zip(standard.Times), pair =>
            Assert.Equal(TimeSpan.FromMinutes(nextSavings), pair.First.LocalTime - pair.Second.LocalTime));
        var after = vm.PrayerRows.Select(row => row.Time).ToArray();
        Assert.NotEqual(before[2], after[2]);
        vm.PreviousDay();
        Assert.Equal(before, vm.PrayerRows.Select(row => row.Time));
        vm.NextDay();
        vm.Tick();
        Assert.Equal(after, vm.PrayerRows.Select(row => row.Time));
        vm.Today();
        Assert.Equal(before, vm.PrayerRows.Select(row => row.Time));
    });
}
