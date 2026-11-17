using AthanTime.Net.Services;
using AthanTime.Net.ViewModels;
using Serilog.Core;
using Serilog.Events;

namespace AthanTime.Tests;

/// <summary>Checks offline defaults and the global reminder pause without native audio.</summary>
[Collection("UI")]
public sealed class BundledReminderTests(DefaultAdhkarRepository defaults, UiTestHost ui, ViewModelHarness harness,
    LoggingLevelSwitch levelSwitch)
{
    [Fact]
    public Task SuggestedCityCannotSendPrayerRemindersBeforeConfirmationOrReplayPastOnes() => ui.Run(() =>
    {
        harness.Settings.Value.LocationNeedsConfirmation = true;
        foreach (var row in harness.Settings.Value.PrayerNotifications.Values)
        { row.NotifyBefore = false; row.NotifyAfter = false; }
        var dhuhr = harness.Settings.Value.PrayerNotifications[AthanTime.Net.Models.Prayer.Dhuhr];
        dhuhr.NotifyAfter = true; dhuhr.MinutesAfter = 1;
        harness.Clock.Now = new DateTimeOffset(2026, 9, 26, 11, 59, 58, TimeSpan.Zero);
        using var vm = harness.Create();
        var alerts = new List<PrayerAlert>();
        vm.PrayerDue += (_, alert) => alerts.Add(alert);
        vm.Tick();
        AdvanceSeconds(vm, 3);
        Assert.Empty(alerts);
        vm.ConfirmLocation();
        var saves = harness.Settings.Saves;
        vm.ConfirmLocation();
        Assert.Equal(saves, harness.Settings.Saves);
        vm.Tick();
        Assert.Empty(alerts);
        AdvanceSeconds(vm, 59);
        Assert.Single(alerts);
        Assert.Equal(PrayerAlertMoment.After, alerts[0].Moment);
        Assert.False(harness.Settings.Value.LocationNeedsConfirmation);
    });

    [Fact]
    public void BundledRecordingsAreLocalOggFiles()
    {
        Assert.True(AudioPlayer.IsSupportedFile(BundledAudio.Athan));
        Assert.True(AudioPlayer.IsSupportedFile(BundledAudio.Takbeer));
    }

    [Theory]
    [InlineData("BUNDLED:ADHAN", "adhan.ogg")]
    [InlineData("Bundled:Takbeer", "takbeer.ogg")]
    public void BundledIdentifiersResolveToInstallationLocalRecordings(string id, string fileName) =>
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "Assets", "Audio", fileName), BundledAudio.Resolve(id));

    [Fact]
    public void CustomPathsAndUnknownIdentifiersAreNotRewritten()
    {
        var path = Path.Combine(Path.GetTempPath(), "custom.ogg");
        Assert.Equal(path, BundledAudio.Resolve(path));
        Assert.Equal("bundled:unknown", BundledAudio.Resolve("bundled:unknown"));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("tr")]
    [InlineData("de")]
    [InlineData("id")]
    [InlineData("ur")]
    public void EveryLanguageHasMorningEveningAndGeneralTexts(string language)
    {
        var morning = defaults.GetTexts(language.ToUpperInvariant(), 8);
        var evening = defaults.GetTexts(language, 18);
        var general = defaults.GetTexts(language, 13);
        Assert.Equal(5, morning.Count);
        Assert.Equal(5, evening.Count);
        Assert.Equal(4, general.Count);
        Assert.All(morning.Concat(evening), text => Assert.Contains('\n', text));
        Assert.NotEqual(morning[0], evening[0]);
    }

    [Fact]
    public Task PauseSuppressesTextAndResumesAfterItsOwnInterval() => ui.Run(() =>
    {
        using var vm = harness.Create();
        vm.TextAdhkarEnabled = true;
        vm.TextAdhkarPauseMinutes = 1;
        vm.NotificationPauseMinutes = 2;
        var shown = new List<string?>();
        vm.TextAdhkarChanged += (_, text) => shown.Add(text);
        vm.ToggleNotificationsPause();
        Assert.True(vm.NotificationsPaused);
        // Ordinary timer ticks preserve intervals; a sleep-sized gap intentionally restarts them.
        vm.Tick();
        AdvanceSeconds(vm, 60);
        Assert.DoesNotContain(shown, text => text is not null);
        AdvanceSeconds(vm, 60);
        Assert.False(vm.NotificationsPaused);
        Assert.Null(harness.Settings.Value.NotificationsPausedUntilUtc);
        AdvanceSeconds(vm, 60);
        Assert.Contains(shown, text => text is not null);
    });

    [Fact]
    public Task DefaultPrayerAudioAndDiagnosticSwitchUseSavedSettings() => ui.Run(() =>
    {
        harness.Calculator.Fajr = new TimeOnly(10, 0);
        harness.Settings.Value.PrayerNotifications[AthanTime.Net.Models.Prayer.Fajr].AudioEnabled = true;
        using var vm = harness.Create();
        vm.Tick();
        Assert.Contains(BundledAudio.Athan, harness.Audio.Played);
        vm.DebugLoggingEnabled = true;
        Assert.Equal(LogEventLevel.Debug, levelSwitch.MinimumLevel);
        Assert.True(harness.Settings.Value.DebugLoggingEnabled);
        vm.DebugLoggingEnabled = false;
        Assert.Equal(LogEventLevel.Information, levelSwitch.MinimumLevel);
    });

    private void AdvanceSeconds(MainViewModel vm, int seconds)
    {
        for (var second = 0; second < seconds; second++)
        {
            harness.Clock.Now = harness.Clock.Now.AddSeconds(1);
            vm.Tick();
        }
    }
}
