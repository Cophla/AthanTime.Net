using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AthanTime.Net.Services;
using AthanTime.Net.ViewModels;

namespace AthanTime.Tests;

/// <summary>Exercises explicit reminder tests through view-model actions and localized buttons without real devices.</summary>
[Collection("UI")]
public sealed class ReminderPreviewTests(UiTestHost ui, ViewModelHarness harness)
{
    [Fact]
    public Task PrayerTestBypassesAutomaticSwitchesWithoutConsumingReminderHistory() => ui.RunAsync(async () =>
    {
        using var vm = harness.Create();
        harness.Settings.Value.NotificationsEnabled = false;
        harness.Settings.Value.PrayerAudioEnabled = true;
        harness.Settings.Value.NotificationsPausedUntilUtc = harness.Clock.Now.AddHours(1);
        foreach (var row in harness.Settings.Value.PrayerNotifications.Values) row.AudioEnabled = true;
        var saves = harness.Settings.Saves;
        var alerts = new List<PrayerAlert>();
        vm.PrayerDue += (_, alert) => alerts.Add(alert);
        await vm.TestPrayerReminderAsync();
        Assert.Single(alerts);
        Assert.Equal(PrayerAlertMoment.AtTime, alerts[0].Moment);
        Assert.Equal(new[] { BundledAudio.Athan }, harness.Audio.Played);
        Assert.Empty(harness.Settings.Value.RecentPrayerAlerts);
        Assert.Equal(saves, harness.Settings.Saves);
        Assert.False(vm.NotificationsEnabled);
        Assert.True(vm.NotificationsPaused);
        harness.Settings.Value.PrayerAudioEnabled = false;
        await vm.TestPrayerReminderAsync();
        Assert.Equal(2, alerts.Count);
        Assert.Single(harness.Audio.Played);
    });

    [Fact]
    public Task RecordingTestUsesSelectionPlaylistAndBundledFallbackWithoutSaving() => ui.RunAsync(async () =>
    {
        using var vm = harness.Create();
        var first = Path.Combine(Path.GetTempPath(), "first-test.ogg");
        var selected = Path.Combine(Path.GetTempPath(), "selected-test.ogg");
        harness.Settings.Value.Adhkar.AudioFiles = [first, selected];
        vm.SelectedAdhkarFile = selected;
        var saves = harness.Settings.Saves;
        await vm.TestAdhkarAudioAsync();
        vm.SelectedAdhkarFile = null;
        await vm.TestAdhkarAudioAsync();
        harness.Settings.Value.Adhkar.AudioFiles.Clear();
        await vm.TestAdhkarAudioAsync();
        Assert.Equal(new[] { selected, first, BundledAudio.Takbeer }, harness.Audio.Played);
        Assert.Equal(saves, harness.Settings.Saves);
        Assert.False(vm.AdhkarEnabled);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task TextTestExpiresAtConfiguredDurationEvenWhenDisabledOrPaused(bool paused) => ui.Run(() =>
    {
        using var vm = harness.Create();
        harness.Settings.Value.TextAdhkar.Enabled = false;
        harness.Settings.Value.TextAdhkar.DisplaySeconds = 5;
        harness.Settings.Value.TextAdhkar.Texts = ["first text", "selected text"];
        if (paused) harness.Settings.Value.NotificationsPausedUntilUtc = harness.Clock.Now.AddHours(1);
        vm.SelectedAdhkarText = "selected text";
        var texts = new List<string?>();
        vm.TextAdhkarChanged += (_, text) => texts.Add(text);
        var saves = harness.Settings.Saves;
        vm.Tick();
        vm.TestTextAdhkar();
        Assert.Equal("selected text", texts.Last());
        for (var second = 1; second <= 5; second++)
        {
            harness.Clock.Now = harness.Clock.Now.AddSeconds(1);
            vm.Tick();
            if (second < 5) Assert.Equal("selected text", texts.Last());
        }
        Assert.Null(texts.Last());
        Assert.Equal(saves, harness.Settings.Saves);
        Assert.False(vm.TextAdhkarEnabled);
        vm.SelectedAdhkarText = null;
        vm.TestTextAdhkar();
        Assert.Equal("first text", texts.Last());
        vm.DismissTextAdhkar();
        Assert.Null(texts.Last());
    });

    [Fact]
    public void TextPreviewDoesNotAdvanceAutomaticRotationAndDismissStartsAFullPause()
    {
        var scheduler = new TextAdhkarScheduler();
        var settings = new AthanTime.Net.Models.TextAdhkarSettings { Enabled = true, PauseMinutes = 1, Texts = ["first", "second"] };
        var now = DateTimeOffset.UtcNow;
        scheduler.Preview("test", settings, now);
        scheduler.Dismiss(settings, now.AddSeconds(1));
        Assert.False(scheduler.IsPreview);
        Assert.False(scheduler.Tick(settings, now.AddSeconds(60)));
        Assert.True(scheduler.Tick(settings, now.AddSeconds(61)));
        Assert.Equal("first", scheduler.CurrentText);
        scheduler.Preview("another", settings, now.AddSeconds(62));
        scheduler.Reset();
        Assert.False(scheduler.IsPreview);
        Assert.Null(scheduler.CurrentText);
    }

    [Fact]
    public Task TestButtonsRenderInEveryLanguageAndThemeAndInvokeTheirActions() => ui.Run(() =>
    {
        using var vm = harness.Create();
        var window = new AthanTime.Net.MainWindow(vm, Microsoft.Extensions.Logging.Abstractions.NullLogger<AthanTime.Net.MainWindow>.Instance) { ForceClose = true };
        try
        {
            window.Show();
            window.FindControl<TabControl>("MainPages")!.SelectedIndex = 1;
            window.FindControl<TabControl>("SettingsPages")!.SelectedIndex = 2;
            string[] names = ["TestPrayerReminderButton", "TestAdhkarAudioButton", "TestTextAdhkarButton"];
            var expected = new Dictionary<string, string>
            {
                ["en"] = "Test", ["ar"] = "اختبار", ["fr"] = "Tester", ["es"] = "Probar",
                ["tr"] = "Test et", ["de"] = "Testen", ["id"] = "Uji", ["ur"] = "آزمائیں"
            };
            foreach (var language in vm.Languages)
            foreach (var dark in new[] { false, true })
            {
                vm.SelectedLanguage = language;
                vm.UseDarkMode = dark;
                for (var page = 0; page < names.Length; page++)
                {
                    window.FindControl<TabControl>("AudioPages")!.SelectedIndex = page;
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    var button = window.FindControl<Button>(names[page])!;
                    Assert.True(button.IsEffectivelyVisible);
                    Assert.Equal(vm["Test"], button.Content);
                    Assert.Equal(expected[language.Code], vm["Test"]);
                }
            }
            var alerts = 0;
            string? reading = null;
            vm.PrayerDue += (_, _) => alerts++;
            vm.TextAdhkarChanged += (_, text) => reading = text;
            for (var page = 0; page < names.Length; page++)
            {
                window.FindControl<TabControl>("AudioPages")!.SelectedIndex = page;
                Dispatcher.UIThread.RunJobs();
                window.FindControl<Button>(names[page])!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            Assert.Equal(1, alerts);
            Assert.Contains(BundledAudio.Takbeer, harness.Audio.Played);
            Assert.False(string.IsNullOrWhiteSpace(reading));
            vm.DismissTextAdhkar();
            Assert.Null(reading);
        }
        finally { window.Close(); }
    });
}
