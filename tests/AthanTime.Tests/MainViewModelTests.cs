using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AthanTime.Net;
using AthanTime.Net.Models;
using AthanTime.Net.Services;
using AthanTime.Net.ViewModels;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Threading.Channels;

namespace AthanTime.Tests;

/// <summary>Exercises real bindings, multilingual selections, timer resource use and reminder delivery.</summary>
[Collection("UI")]
public sealed class MainViewModelTests(UiTestHost ui, ViewModelHarness harness, ILogger<MainWindow> logger)
{
    [Fact]
    public Task VolumeSavesOnlyActualClampedChanges() => ui.Run(() =>
    {
        using var vm = harness.Create();
        var saves = harness.Settings.Saves;
        var changes = 0;
        vm.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(vm.AudioVolume)) changes++; };
        for (var assignment = 0; assignment < 5; assignment++) vm.AudioVolume = vm.AudioVolume;
        Assert.Equal(saves, harness.Settings.Saves);
        Assert.Equal(0, changes);
        vm.AudioVolume = 150;
        Assert.Equal(100, vm.AudioVolume);
        Assert.Equal(saves + 1, harness.Settings.Saves);
        vm.AudioVolume = 100;
        vm.AudioVolume = int.MaxValue;
        Assert.Equal(saves + 1, harness.Settings.Saves);
        vm.AudioVolume = -1;
        Assert.Equal(0, vm.AudioVolume);
        vm.AudioVolume = int.MinValue;
        Assert.Equal(saves + 2, harness.Settings.Saves);
        vm.AudioVolume = 65;
        Assert.Equal(65, harness.Settings.Value.AudioVolume);
        Assert.Equal(saves + 3, harness.Settings.Saves);
        Assert.Equal(3, changes);
    });

    [Fact]
    public Task SlowSearchCoalescesToTheLatestQueryAndLanguage() => ui.RunAsync(async () =>
    {
        using var locations = new ControlledSearchRepository();
        using var vm = harness.Create(locations);
        vm.SearchText = "be";
        vm.SelectedLanguage = vm.Languages.Single(language => language.Code == "fr");
        var first = await locations.NextAsync();
        var completion = SearchCompletion(vm);
        vm.SearchText = "lon";
        vm.SelectedLanguage = vm.Languages.Single(language => language.Code == "ar");
        vm.SearchText = "ber";
        vm.SelectedLanguage = vm.Languages.Single(language => language.Code == "de");
        Assert.Same(completion, SearchCompletion(vm));
        first.Result.SetResult([new("old", "Obsolete", "", 0, 0, "Etc/UTC")]);
        var latest = await locations.NextAsync();
        Assert.Equal("ber", latest.Query);
        Assert.Equal("de", latest.Language);
        Assert.Empty(vm.SearchResults);
        var updatedOnUiThread = false;
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.SearchResults)) updatedOnUiThread = Dispatcher.UIThread.CheckAccess();
        };
        latest.Result.SetResult([new("new", "Berlin", "DE", 52.52, 13.4, "Europe/Berlin",
            new Dictionary<string, string> { ["de"] = "Berlin latest" })]);
        await completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("Berlin latest", Assert.Single(vm.SearchResults).Title);
        Assert.True(updatedOnUiThread);
        Assert.Equal(2, locations.CallCount);
        Assert.Equal(1, locations.MaximumActive);
    });

    [Fact]
    public Task ResultNotificationCanQueueTheNextLanguageWithoutAnotherWorker() => ui.RunAsync(async () =>
    {
        using var locations = new ControlledSearchRepository();
        using var vm = harness.Create(locations);
        vm.SearchText = "be";
        vm.SelectedLanguage = vm.Languages.Single(language => language.Code == "fr");
        var first = await locations.NextAsync();
        var completion = SearchCompletion(vm);
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.SearchResults) && vm.SelectedLanguage.Code == "fr")
                vm.SelectedLanguage = vm.Languages.Single(language => language.Code == "de");
        };
        first.Result.SetResult([new("first", "First result", "", 0, 0, "Etc/UTC")]);
        var next = await locations.NextAsync();
        Assert.Same(completion, SearchCompletion(vm));
        Assert.Equal("de", next.Language);
        next.Result.SetResult([new("next", "Next result", "", 0, 0, "Etc/UTC")]);
        await completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("Next result", Assert.Single(vm.SearchResults).Title);
        Assert.Equal(2, locations.CallCount);
        Assert.Equal(1, locations.MaximumActive);
    });

    [Fact]
    public Task DisposalBeforeTheSearchWorkerStartsDoesNotOpenAQuery() => ui.RunAsync(async () =>
    {
        using var locations = new ControlledSearchRepository();
        using var vm = harness.Create(locations);
        vm.SearchText = "be";
        vm.SelectedLanguage = vm.Languages.Single(language => language.Code == "fr");
        var completion = SearchCompletion(vm);
        vm.Dispose();
        await completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, locations.CallCount);
        Assert.Empty(vm.SearchResults);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task FailedSearchAllowsQueuedOrLaterRequests(bool queueBeforeFailure) => ui.RunAsync(async () =>
    {
        using var locations = new ControlledSearchRepository();
        using var vm = harness.Create(locations);
        vm.SearchText = "be";
        vm.SelectedLanguage = vm.Languages.Single(language => language.Code == "fr");
        var first = await locations.NextAsync();
        var completion = SearchCompletion(vm);
        if (queueBeforeFailure) vm.SelectedLanguage = vm.Languages.Single(language => language.Code == "de");
        first.Result.SetException(new IOException("Controlled lookup failure."));
        if (!queueBeforeFailure)
        {
            await completion.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Empty(vm.SearchResults);
            vm.SelectedLanguage = vm.Languages.Single(language => language.Code == "de");
            completion = SearchCompletion(vm);
        }
        var next = await locations.NextAsync();
        Assert.Equal("de", next.Language);
        next.Result.SetResult([new("recovered", "Recovered", "", 0, 0, "Etc/UTC")]);
        await completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("Recovered", Assert.Single(vm.SearchResults).Title);
        Assert.Equal(2, locations.CallCount);
        Assert.Equal(1, locations.MaximumActive);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ClearingOrDisposingSearchDropsPendingWorkAndLateResults(bool dispose) => ui.RunAsync(async () =>
    {
        using var locations = new ControlledSearchRepository();
        using var vm = harness.Create(locations);
        vm.SearchText = "be";
        vm.SelectedLanguage = vm.Languages.Single(language => language.Code == "fr");
        var first = await locations.NextAsync();
        var completion = SearchCompletion(vm);
        vm.SelectedLanguage = vm.Languages.Single(language => language.Code == "de");
        if (dispose) vm.Dispose();
        else vm.SearchText = "";
        var updates = 0;
        vm.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(vm.SearchResults)) updates++; };
        first.Result.SetResult([new("late", "Late result", "", 0, 0, "Etc/UTC")]);
        await completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(vm.SearchResults);
        Assert.Equal(0, updates);
        Assert.Equal(1, locations.CallCount);
    });

    [Fact]
    public Task OrdinaryTicksNotifyOnlyChangedValuesAndKeepCachedRows() => ui.Run(() =>
    {
        using var vm = harness.Create();
        vm.Tick();
        var rows = vm.PrayerRows;
        var calls = harness.Calculator.Calls;
        var saves = harness.Settings.Saves;
        var changed = new List<string?>();
        vm.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        harness.Clock.Now = harness.Clock.Now.AddSeconds(1);
        vm.Tick();
        Assert.Contains(nameof(vm.CurrentClock), changed);
        Assert.Contains(nameof(vm.Countdown), changed);
        Assert.Contains(nameof(vm.DayProgress), changed);
        Assert.DoesNotContain(nameof(vm.NextPrayerName), changed);
        Assert.DoesNotContain(nameof(vm.NextPrayerTime), changed);
        Assert.DoesNotContain(nameof(vm.SummerTimeEnabled), changed);
        Assert.DoesNotContain(nameof(vm.ActiveSummerTimeEnabled), changed);
        Assert.DoesNotContain(nameof(vm.ActiveSummerTimeDescription), changed);
        Assert.Same(rows, vm.PrayerRows);
        Assert.Equal(calls, harness.Calculator.Calls);
        Assert.Equal(saves, harness.Settings.Saves);
        changed.Clear();
        vm.Tick();
        Assert.Empty(changed);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task NextPrayerNotificationsFollowPrayerBoundariesButExcludeSunrise(bool sunrise) => ui.Run(() =>
    {
        harness.Clock.Now = new DateTimeOffset(2026, 9, 26, sunrise ? 5 : 11, 59, 59, TimeSpan.Zero);
        using var vm = harness.Create();
        vm.Tick();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        harness.Clock.Now = harness.Clock.Now.AddSeconds(1);
        vm.Tick();
        Assert.Equal(vm[sunrise ? "Dhuhr" : "AsrPrayer"], vm.NextPrayerName);
        if (sunrise)
        {
            Assert.DoesNotContain(nameof(vm.NextPrayerName), changed);
            Assert.DoesNotContain(nameof(vm.NextPrayerTime), changed);
        }
        else
        {
            Assert.Contains(nameof(vm.NextPrayerName), changed);
            Assert.Contains(nameof(vm.NextPrayerTime), changed);
        }
    });

    [Fact]
    public Task AnotherSelectedDateKeepsItsDisplayWhileTheLiveTrayAdvances() => ui.Run(() =>
    {
        harness.Clock.Now = new DateTimeOffset(2026, 9, 26, 11, 59, 59, TimeSpan.Zero);
        using var vm = harness.Create();
        vm.NextDay();
        vm.Tick();
        var name = vm.NextPrayerName;
        var time = vm.NextPrayerTime;
        var changed = new List<string?>();
        string? tray = null;
        vm.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        vm.TrayTextChanged += (_, text) => tray = text;
        harness.Clock.Now = harness.Clock.Now.AddSeconds(1);
        vm.Tick();
        Assert.Equal(name, vm.NextPrayerName);
        Assert.Equal(time, vm.NextPrayerTime);
        Assert.Empty(vm.Countdown);
        Assert.Equal(0, vm.DayProgress);
        Assert.DoesNotContain(nameof(vm.NextPrayerName), changed);
        Assert.DoesNotContain(nameof(vm.NextPrayerTime), changed);
        Assert.NotNull(tray);
        Assert.Contains(vm["AsrPrayer"], tray);
    });

    [Fact]
    public Task AnUnappliedDraftBoundaryNotifiesWithoutChangingTheCommittedClock() => ui.Run(() =>
    {
        harness.Clock.Now = new DateTimeOffset(2026, 3, 29, 1, 59, 59, TimeSpan.Zero);
        using var vm = harness.Create();
        vm.SelectedSummerTimeMode = vm.SummerTimeModes.Single(item => item.Value == SummerTimeMode.CustomDates);
        vm.Tick();
        Assert.False(vm.SummerTimeEnabled);
        Assert.False(vm.ActiveSummerTimeEnabled);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        harness.Clock.Now = harness.Clock.Now.AddSeconds(1);
        vm.Tick();
        Assert.True(vm.SummerTimeEnabled);
        Assert.False(vm.ActiveSummerTimeEnabled);
        Assert.Contains(nameof(vm.SummerTimeEnabled), changed);
        Assert.DoesNotContain(nameof(vm.ActiveSummerTimeEnabled), changed);
        Assert.DoesNotContain(nameof(vm.ActiveSummerTimeDescription), changed);
    });

    [Fact]
    public Task CommittedMinuteChangesStillNotifyWhenBothOffsetsAreActive() => ui.Run(() =>
    {
        using var vm = harness.Create();
        vm.AutomaticSummerTime = false;
        vm.SummerTimeEnabled = true;
        vm.ApplySummerTimeChanges();
        vm.Tick();
        Assert.True(vm.ActiveSummerTimeEnabled);
        var before = vm.ActiveSummerTimeDescription;
        vm.SummerTimeMinutes = 30;
        vm.ApplySummerTimeChanges();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        harness.Clock.Now = harness.Clock.Now.AddSeconds(1);
        vm.Tick();
        Assert.True(vm.ActiveSummerTimeEnabled);
        Assert.NotEqual(before, vm.ActiveSummerTimeDescription);
        Assert.Contains(nameof(vm.ActiveSummerTimeDescription), changed);
        Assert.DoesNotContain(nameof(vm.ActiveSummerTimeEnabled), changed);
    });

    /// <summary>The beta channel is rendered in every language/theme; About retains the full semantic version.</summary>
    [Fact]
    public Task ReleaseIdentityIsVisibleAndLocalized() => ui.Run(() =>
    {
        using var vm = harness.Create();
        var window = new MainWindow(vm, logger) { Width = 980, Height = 600, ForceClose = true };
        window.Show();
        try
        {
            var expectedLabels = new Dictionary<string, string>
            {
                ["en"] = "Beta", ["ar"] = "نسخة تجريبية", ["fr"] = "Bêta", ["es"] = "Beta",
                ["tr"] = "Beta", ["de"] = "Beta", ["id"] = "Beta", ["ur"] = "بیٹا"
            };
            var assembly = typeof(App).Assembly;
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
            Assert.Contains(informational, assembly.GetCustomAttribute<AssemblyProductAttribute>()!.Product);
            Assert.Contains(informational, assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()!.Description);
            Assert.Equal(informational, vm.AppVersion);
            Assert.DoesNotContain('+', vm.AppVersion);
            bool expectedBeta = informational.EndsWith("-beta", StringComparison.OrdinalIgnoreCase)
                || informational.Contains("-beta.", StringComparison.OrdinalIgnoreCase);
            Assert.Equal(expectedBeta, App.IsBeta);
            foreach (var language in vm.Languages)
            foreach (var dark in new[] { false, true })
            {
                vm.SelectedLanguage = language;
                vm.UseDarkMode = dark;
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var header = window.FindControl<TextBlock>("ApplicationName")!;
                Assert.Equal(vm.AppName, window.Title);
                Assert.Equal(vm.AppName, header.Text);
                Assert.True(header.IsEffectivelyVisible);
                Assert.Contains(vm.AppVersion, vm.AboutText);
                if (expectedBeta) Assert.Contains($"({expectedLabels[language.Code]})", header.Text);
                else Assert.DoesNotContain($"({expectedLabels[language.Code]})", header.Text);
                Assert.True(header.Bounds.Width > 0);
                var first = header.TranslatePoint(new Point(0, 0), window)!.Value;
                var last = header.TranslatePoint(new Point(header.Bounds.Width, header.Bounds.Height), window)!.Value;
                Assert.InRange(Math.Min(first.X, last.X), 0, window.ClientSize.Width);
                Assert.InRange(Math.Max(first.X, last.X), 0, window.ClientSize.Width);
            }
        }
        finally { window.Close(); }
    });

    /// <summary>All three rendered switches and both durations save independently across languages/themes.</summary>
    [Theory]
    [InlineData(Prayer.Fajr)]
    [InlineData(Prayer.Sunrise)]
    public Task NotificationSettingsKeepIndependentChoicesInEveryLanguage(Prayer prayer) => ui.Run(() =>
    {
        using var vm = harness.Create();
        var window = new MainWindow(vm, logger) { Width = 980, Height = 600, ForceClose = true };
        window.Show();
        try
        {
            ShowSettings(window, 2);
            window.FindControl<TabControl>("AudioPages")!.SelectedIndex = 0;
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var row = vm.NotificationRows.Single(item => item.Prayer == prayer);
            var expander = window.GetVisualDescendants().OfType<Expander>().Single(control => ReferenceEquals(control.DataContext, row));
            expander.IsExpanded = true;
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var input = expander.GetVisualDescendants().OfType<NumericUpDown>().Single(control => control.Name == "BeforePrayerMinutesInput");
            var afterInput = expander.GetVisualDescendants().OfType<NumericUpDown>().Single(control => control.Name == "AfterPrayerMinutesInput");
            var switches = expander.GetVisualDescendants().OfType<CheckBox>().Where(control => control.Name is "BeforePrayerSwitch" or "AtPrayerSwitch" or "AfterPrayerSwitch")
                .ToDictionary(control => control.Name!);
            Assert.Equal(15m, input.Value);
            Assert.Equal(5m, afterInput.Value);
            Assert.False(afterInput.IsEnabled);
            var saves = harness.Settings.Saves;
            input.Value = 30;
            switches["AfterPrayerSwitch"].IsChecked = true;
            switches["AtPrayerSwitch"].IsChecked = false;
            Dispatcher.UIThread.RunJobs();
            Assert.True(afterInput.IsEnabled);
            afterInput.Value = 10;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(30, row.MinutesBefore);
            Assert.Equal(10, row.MinutesAfter);
            Assert.True(row.NotifyBefore);
            Assert.True(row.NotifyAfter);
            Assert.False(row.NotifyAtTime);
            Assert.Equal(30, harness.Settings.Value.PrayerNotifications[prayer].MinutesBefore);
            Assert.Equal(10, harness.Settings.Value.PrayerNotifications[prayer].MinutesAfter);
            Assert.True(harness.Settings.Saves > saves);
            foreach (var language in vm.Languages)
            foreach (var dark in new[] { false, true })
            {
                vm.SelectedLanguage = language;
                vm.UseDarkMode = dark;
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                Assert.Equal(30m, input.Value);
                Assert.Equal(10m, afterInput.Value);
                Assert.Equal(FlowDirection.LeftToRight, input.FlowDirection);
                Assert.Equal(FlowDirection.LeftToRight, afterInput.FlowDirection);
                Assert.True(input.Bounds.Width >= 100);
                Assert.True(afterInput.Bounds.Width >= 100);
                Assert.True(switches["BeforePrayerSwitch"].IsChecked);
                Assert.True(switches["AfterPrayerSwitch"].IsChecked);
                Assert.False(switches["AtPrayerSwitch"].IsChecked);
                Assert.Contains(expander.GetVisualDescendants().OfType<CheckBox>(), control => Equals(control.Content, vm[prayer == Prayer.Sunrise ? "NotifyBeforeSunrise" : "NotifyBefore"]));
                Assert.Equal(vm[prayer == Prayer.Sunrise ? "NotifyAtSunrise" : "NotifyAtTime"], switches["AtPrayerSwitch"].Content);
                Assert.Equal(vm[prayer == Prayer.Sunrise ? "NotifyAfterSunrise" : "NotifyAfter"], switches["AfterPrayerSwitch"].Content);
                Assert.Contains(expander.GetVisualDescendants().OfType<TextBlock>(), control => control.Text == vm["BeforeMinutes"]);
                Assert.Contains(expander.GetVisualDescendants().OfType<TextBlock>(), control => control.Text == vm["AfterMinutes"]);
                Assert.Contains(expander.GetVisualDescendants().OfType<TextBlock>(), control => control.Text == vm[prayer == Prayer.Sunrise ? "AtSunriseTiming" : "AtPrayerTiming"]);
                Assert.Same(row, vm.NotificationRows.Single(item => item.Prayer == prayer));
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), control => control.Text == vm["NotificationSettings"]);
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), control => control.Text == vm["NotificationTimingHelp"]);
            }
            switches["BeforePrayerSwitch"].IsChecked = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(input.IsEnabled);
            Assert.True(afterInput.IsEnabled);
            switches["AfterPrayerSwitch"].IsChecked = false;
            switches["AtPrayerSwitch"].IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.False(afterInput.IsEnabled);
            Assert.True(row.NotifyAtTime);
            Assert.False(row.NotifyBefore);
            Assert.False(row.NotifyAfter);
            Assert.Equal(30, row.MinutesBefore);
            Assert.Equal(10, row.MinutesAfter);
            var master = window.FindControl<CheckBox>("NotificationMasterSwitch")!;
            master.IsChecked = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(vm.NotificationsEnabled);
            Assert.True(row.NotifyAtTime);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task SeasonalCheckboxFollowsTheInstantAndManualPreferencesStayWithTheirZone() => ui.Run(() =>
    {
        harness.Clock.Now = new DateTimeOffset(2026, 3, 29, 0, 59, 59, TimeSpan.Zero);
        var london = new LocationInfo("summer-london", "London", "GB", 51.5074, -0.1278, "Europe/London");
        harness.Settings.Value.SavedLocations = [london];
        harness.Settings.Value.LocationId = london.Id;
        using var vm = harness.Create();
        Assert.True(vm.AutomaticSummerTime);
        Assert.False(vm.SummerTimeEnabled);
        var notified = false;
        vm.PropertyChanged += (_, args) => notified |= args.PropertyName == nameof(vm.SummerTimeEnabled);
        vm.Tick();
        harness.Clock.Now = harness.Clock.Now.AddSeconds(1);
        vm.Tick();
        Assert.True(vm.SummerTimeEnabled);
        Assert.True(notified);
        vm.AutomaticSummerTime = false;
        vm.SummerTimeEnabled = false;
        vm.ApplySummerTimeChanges();
        vm.ManualName = "Riyadh"; vm.ManualLatitude = 24.7136m; vm.ManualLongitude = 46.6753m;
        vm.ManualCountry = "SA"; vm.ManualTimeZone = "Asia/Riyadh"; vm.AddManualLocation();
        Assert.True(vm.AutomaticSummerTime);
        Assert.False(vm.SummerTimeEnabled);
        vm.SelectedSavedLocation = london;
        Assert.False(vm.AutomaticSummerTime);
        Assert.False(vm.SummerTimeEnabled);
        vm.AutomaticSummerTime = true;
        Assert.True(vm.SummerTimeEnabled);
    });

    [Fact]
    public Task CustomSeasonChangesTheCheckboxWithoutRecalculatingPrayerAstronomy() => ui.Run(() =>
    {
        harness.Clock.Now = new DateTimeOffset(2026, 3, 29, 1, 59, 59, TimeSpan.Zero);
        using var vm = harness.Create();
        vm.SelectedSummerTimeMode = vm.SummerTimeModes.Single(item => item.Value == SummerTimeMode.CustomDates);
        vm.ApplySummerTimeChanges();
        Assert.False(vm.SummerTimeEnabled);
        vm.Tick();
        var calls = harness.Calculator.Calls;
        var saves = harness.Settings.Saves;
        harness.Clock.Now = harness.Clock.Now.AddSeconds(1);
        vm.Tick();
        Assert.True(vm.SummerTimeEnabled);
        Assert.Equal(calls, harness.Calculator.Calls);
        Assert.Equal(saves, harness.Settings.Saves);
        vm.SummerTimeEndEditor.SelectedMonth = vm.SummerTimeStartEditor.SelectedMonth;
        vm.SummerTimeEndEditor.Hour = vm.SummerTimeStartEditor.Hour;
        Assert.NotEmpty(vm.SummerTimeError);
        Assert.False(vm.SummerTimeEnabled);
        Assert.False(vm.AutomaticSummerTime);
        Assert.True(vm.CustomSummerTime);
        Assert.False(vm.CanApplySummerTime);
        Assert.True(vm.ActiveSummerTimeEnabled);
    });

    [Fact]
    public Task OfflineSearch_ResolvesCaseInsensitiveResultsWithoutBlockingTheWindow() => ui.RunAsync(async () =>
    {
        using var vm = harness.Create();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.SearchResults) && vm.SearchResults.Count > 0) ready.TrySetResult();
        };
        vm.SearchText = "sIdOn";
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains(vm.SearchResults, item => item.Title.Equals("Sidon", StringComparison.OrdinalIgnoreCase));
        vm.SelectedLanguage = vm.Languages.Single(language => language.Code.Equals("AR", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("sIdOn", vm.SearchText);
        var localized = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.SearchResults) && vm.SearchResults.Count > 0) localized.TrySetResult();
        };
        await localized.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotEmpty(vm.SearchResults);
        vm.SearchText = "London";
        vm.SearchText = "";
        await Task.Delay(350);
        Assert.Empty(vm.SearchResults);
    });

    [Fact]
    public Task CustomSummerRuleControlsAreUsableAndLocalizedAtMinimumWindowSize() => ui.Run(() =>
    {
        using var vm = harness.Create();
        var window = new MainWindow(vm, logger) { Width = 980, Height = 600, ForceClose = true };
        window.Show();
        try
        {
            ShowSettings(window, 1);
            var mode = window.FindControl<ComboBox>("SummerTimeModeInput")!;
            mode.SelectedItem = vm.SummerTimeModes.Single(item => item.Value == SummerTimeMode.CustomDates);
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.True(vm.CustomSummerTime);
            var start = window.FindControl<ContentControl>("SummerStartEditor")!;
            var kind = start.GetVisualDescendants().OfType<ComboBox>().Single(control => control.Name == "RuleKindInput");
            kind.SelectedItem = vm.SummerTimeStartEditor.Kinds.Single(item => !item.Value);
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var day = start.GetVisualDescendants().OfType<NumericUpDown>().Single(control => control.Name == "RuleDayInput");
            day.Value = 15;
            Assert.False(vm.SummerTimeStartEditor.IsWeekday);
            Assert.Equal(15, vm.SummerTimeStartEditor.Day);
            kind.SelectedItem = vm.SummerTimeStartEditor.Kinds.Single(item => item.Value);
            var occurrence = start.GetVisualDescendants().OfType<ComboBox>().Single(control => control.Name == "RuleOccurrenceInput");
            occurrence.SelectedItem = vm.SummerTimeStartEditor.Occurrences.Single(item => item.Value == 1);
            var weekday = start.GetVisualDescendants().OfType<ComboBox>().Single(control => control.Name == "RuleWeekdayInput");
            weekday.SelectedItem = vm.SummerTimeStartEditor.Weekdays.Single(item => item.Value == DayOfWeek.Thursday);
            Assert.Equal(DayOfWeek.Thursday, vm.SummerTimeStartEditor.SelectedWeekday.Value);
            foreach (var language in vm.Languages)
            foreach (var dark in new[] { false, true })
            {
                vm.SelectedLanguage = language; vm.UseDarkMode = dark;
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                Assert.True(vm.CustomSummerTime);
                Assert.True(vm.SummerTimeStartEditor.IsWeekday);
                Assert.Equal(1, vm.SummerTimeStartEditor.SelectedOccurrence.Value);
                Assert.DoesNotContain("Summer", vm.SummerTimeStartEditor.SelectedOccurrence.Label);
                Assert.Equal(vm.SelectedSummerTimeMode, mode.SelectedItem);
                Assert.Equal(vm["SummerMonth"], start.GetVisualDescendants().OfType<TextBlock>().Single(control => control.Name == "RuleMonthLabel").Text);
                Assert.Equal(vm["SummerChooseDay"], start.GetVisualDescendants().OfType<TextBlock>().Single(control => control.Name == "RuleChooseDayLabel").Text);
                occurrence.BringIntoView(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                Assert.True(occurrence.Bounds.Width >= 220, $"Occurrence width in {language.Code}: {occurrence.Bounds.Width}; start: {start.Bounds}; visible: {occurrence.IsEffectivelyVisible}");
                Assert.True(weekday.Bounds.Width >= 220, $"Weekday width in {language.Code}: {weekday.Bounds.Width}");
                foreach (var input in start.GetVisualDescendants().OfType<NumericUpDown>().Where(input => input.IsEffectivelyVisible))
                {
                    Assert.True(input.Bounds.Width >= 110);
                    Assert.Equal(FlowDirection.LeftToRight, input.FlowDirection);
                    Assert.True(input.GetVisualDescendants().OfType<TextBox>().First().Bounds.Width >= 65);
                }
                occurrence.BringIntoView(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var point = occurrence.TranslatePoint(default, window)!.Value;
                var opposite = occurrence.TranslatePoint(new Point(occurrence.Bounds.Width, occurrence.Bounds.Height), window)!.Value;
                // RTL mirrors local coordinates: (0,0) is not necessarily the physical left edge.
                Assert.InRange(Math.Min(point.X, opposite.X), 0, window.Width);
                Assert.InRange(Math.Max(point.X, opposite.X), 0, window.Width);
                Assert.InRange(Math.Min(point.Y, opposite.Y), 0, window.Height);
                Assert.InRange(Math.Max(point.Y, opposite.Y), 0, window.Height);
                if (string.Equals(language.Code, "en", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(language.Code, "ar", StringComparison.OrdinalIgnoreCase))
                {
                    var directory = Path.Combine(AppContext.BaseDirectory, "ui-captures");
                    Directory.CreateDirectory(directory);
                    using var frame = window.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    frame.Save(Path.Combine(directory, $"summer-{language.Code}-{(dark ? "dark" : "light")}.png"), PngBitmapEncoderOptions.Default);
                    var hour = start.GetVisualDescendants().OfType<NumericUpDown>().Single(control => control.Name == "RuleHourInput");
                    hour.BringIntoView(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                    using var timeFrame = window.CaptureRenderedFrame();
                    Assert.NotNull(timeFrame);
                    timeFrame.Save(Path.Combine(directory, $"summer-time-{language.Code}-{(dark ? "dark" : "light")}.png"), PngBitmapEncoderOptions.Default);
                }
            }
            mode.SelectedItem = vm.SummerTimeModes.Single(item => item.Value == SummerTimeMode.Automatic);
            Assert.True(vm.AutomaticSummerTime);
            Assert.False(window.FindControl<StackPanel>("CustomSummerTimePanel")!.IsVisible);
            vm.SummerTimeMinutes = 30;
            mode.SelectedItem = vm.SummerTimeModes.Single(item => item.Value == SummerTimeMode.CustomDates);
            Assert.Equal(30, vm.SummerTimeMinutes);
            Assert.Equal(1, vm.SummerTimeStartEditor.SelectedOccurrence.Value);
            Assert.Equal(DayOfWeek.Thursday, vm.SummerTimeStartEditor.SelectedWeekday.Value);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task LanguageChanges_PreserveEveryOptionAndRenderReadableNumberFields() => ui.Run(() =>
    {
        using var vm = harness.Create();
        vm.SelectedMethodOption = vm.MethodOptions.First(option => option.Value.Id.Equals("Egypt", StringComparison.OrdinalIgnoreCase));
        vm.SelectedAsr = vm.AsrOptions.Single(option => option.Value == AsrJuristic.Hanafi);
        vm.SelectedHighLatitude = vm.HighLatitudeOptions.Single(option => option.Value == HighLatitudeRule.OneSeventh);
        vm.HijriAdjustment = 1;
        vm.Use24HourClock = true; vm.StartMinimized = true; vm.AdhkarRandomOrder = true; vm.AdhkarIntervalMinutes = 42;
        vm.AudioVolume = 65;
        vm.ManualName = "Saved city"; vm.ManualLatitude = -33.8688m; vm.ManualLongitude = 151.2093m;
        vm.ManualTimeZone = "Australia/Sydney"; vm.ManualCountry = "au"; vm.AddManualLocation();
        vm.FajrAdjustment = -12; vm.SunriseAdjustment = 7;
        vm.AutomaticSummerTime = false; vm.SummerTimeEnabled = true; vm.SummerTimeMinutes = 30;
        vm.SelectedSummerTimeMode = vm.SummerTimeModes.Single(item => item.Value == SummerTimeMode.CustomDates);
        var summerMode = vm.SelectedSummerTimeMode;
        var startEditor = vm.SummerTimeStartEditor;
        startEditor.SelectedOccurrence = startEditor.Occurrences.Single(item => item.Value == 1);
        startEditor.SelectedWeekday = startEditor.Weekdays.Single(item => item.Value == DayOfWeek.Thursday);
        startEditor.Hour = 1; startEditor.Minute = 15;
        var occurrence = startEditor.SelectedOccurrence;
        var weekday = startEditor.SelectedWeekday;
        var asr = vm.SelectedAsr;
        var method = vm.SelectedMethodOption;
        var high = vm.SelectedHighLatitude;
        var saved = vm.SelectedSavedLocation;
        var row = vm.NotificationRows[1];
        row.NotifyAfter = true; row.MinutesAfter = 17; row.AudioEnabled = true; row.AudioPath = "custom.ogg";
        var window = new MainWindow(vm, logger) { Width = 980, Height = 760, ForceClose = true };
        window.Show();
        try
        {
            ShowSettings(window, 1);
            foreach (var language in vm.Languages)
            foreach (var dark in new[] { false, true })
            {
                vm.SelectedLanguage = language;
                vm.UseDarkMode = dark;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.Same(asr, vm.SelectedAsr);
                Assert.Same(method, vm.SelectedMethodOption);
                Assert.Same(method, window.FindControl<ComboBox>("MethodSelector")!.SelectedItem);
                Assert.Same(high, vm.SelectedHighLatitude);
                Assert.Same(saved, vm.SelectedSavedLocation);
                Assert.Same(row, vm.NotificationRows[1]);
                Assert.Same(asr, window.FindControl<ComboBox>("AsrSelector")!.SelectedItem);
                Assert.Same(high, window.FindControl<ComboBox>("HighLatitudeSelector")!.SelectedItem);
                Assert.Same(saved, window.FindControl<ComboBox>("SavedLocationSelector")!.SelectedItem);
                Assert.Equal("Egypt", vm.SelectedMethod.Id);
                Assert.Equal(-12, vm.FajrAdjustment); Assert.Equal(7, vm.SunriseAdjustment);
                Assert.Equal(1, vm.HijriAdjustment); Assert.True(vm.Use24HourClock); Assert.True(vm.StartMinimized);
                Assert.True(vm.AdhkarRandomOrder); Assert.Equal(42, vm.AdhkarIntervalMinutes);
                Assert.Equal(65, vm.AudioVolume);
                Assert.False(vm.AutomaticSummerTime); Assert.True(vm.SummerTimeEnabled); Assert.Equal(30, vm.SummerTimeMinutes);
                Assert.Same(summerMode, window.FindControl<ComboBox>("SummerTimeModeInput")!.SelectedItem);
                Assert.True(vm.CustomSummerTime);
                Assert.Same(startEditor, vm.SummerTimeStartEditor);
                Assert.Same(occurrence, startEditor.SelectedOccurrence);
                Assert.Same(weekday, startEditor.SelectedWeekday);
                Assert.Equal(1, startEditor.Hour); Assert.Equal(15, startEditor.Minute);
                Assert.False(window.FindControl<CheckBox>("SummerTimeInput")!.IsEnabled);
                Assert.True(window.FindControl<CheckBox>("SummerTimeInput")!.IsChecked);
                Assert.True(row.NotifyAfter); Assert.Equal(17, row.MinutesAfter);
                Assert.True(row.AudioEnabled); Assert.Equal("custom.ogg", row.AudioPath);
                Assert.Equal(language.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, window.FlowDirection);
                var input = window.FindControl<NumericUpDown>("FajrAdjustmentInput")!;
                foreach (var expander in window.GetVisualDescendants().OfType<Expander>()) expander.IsExpanded = true;
                window.UpdateLayout();
                Assert.False(input.ShowButtonSpinner);
                Assert.Equal(FlowDirection.LeftToRight, input.FlowDirection);
                Assert.True(input.Bounds.Width >= 100);
                var editor = input.GetVisualDescendants().OfType<TextBox>().First();
                Assert.Contains("12", editor.Text);
                Assert.True(editor.Bounds.Width >= 65, $"Numeric editor too narrow in {language.Code}: {editor.Bounds.Width}");
                if (language.Code is "en" or "ar")
                {
                    var directory = Path.Combine(AppContext.BaseDirectory, "ui-captures");
                    Directory.CreateDirectory(directory);
                    using var frame = window.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    frame.Save(Path.Combine(directory, $"{language.Code}-{(dark ? "dark" : "light")}.png"), PngBitmapEncoderOptions.Default);
                }
            }
        }
        finally { window.Close(); }
    });

    /// <summary>All seasonal changes remain a language-stable draft until a single explicit commit.</summary>
    [Fact]
    public Task SummerTimeDraftRequiresApplyAndCancelRestoresTheActiveRules() => ui.Run(() =>
    {
        using var vm = harness.Create();
        var calls = harness.Calculator.Calls;
        var saves = harness.Settings.Saves;
        vm.SelectedSummerTimeMode = vm.SummerTimeModes.Single(option => option.Value == SummerTimeMode.CustomDates);
        vm.SummerTimeMinutes = 30;
        vm.SummerTimeStartEditor.Minute = 15;
        Assert.True(vm.CanApplySummerTime);
        Assert.Equal(calls, harness.Calculator.Calls);
        Assert.Equal(saves, harness.Settings.Saves);
        Assert.True(harness.Settings.Value.SummerTimeOverrides["Etc/UTC"].Automatic);
        foreach (var language in vm.Languages)
        {
            vm.SelectedLanguage = language;
            Assert.True(vm.SummerTimeHasChanges);
            Assert.Equal(15, vm.SummerTimeStartEditor.Minute);
        }
        vm.CancelSummerTimeChanges();
        Assert.True(vm.AutomaticSummerTime);
        Assert.False(vm.SummerTimeHasChanges);
        vm.AutomaticSummerTime = false;
        vm.SummerTimeEnabled = true;
        vm.SummerTimeMinutes = 30;
        saves = harness.Settings.Saves;
        vm.ApplySummerTimeChanges();
        Assert.Equal(saves + 1, harness.Settings.Saves);
        Assert.False(vm.SummerTimeHasChanges);
        Assert.True(vm.ActiveSummerTimeEnabled);
        vm.RestoreAutomaticSummerTime();
        Assert.True(vm.SummerTimeHasChanges);
        Assert.True(vm.ActiveSummerTimeEnabled);
        vm.ApplySummerTimeChanges();
        Assert.False(vm.ActiveSummerTimeEnabled);
    });

    /// <summary>Explicit corrections follow stable location IDs rather than translated names.</summary>
    [Fact]
    public Task PrayerCorrectionsStayWithTheirLocationAcrossLanguageChangesAndRestart() => ui.Run(() =>
    {
        var first = harness.Settings.Value.SavedLocations[0];
        using (var vm = harness.Create())
        {
            vm.MaghribAdjustment = 5;
            vm.SunriseAdjustment = -2;
            vm.ManualName = "Other place"; vm.ManualLatitude = 40; vm.ManualLongitude = 20;
            vm.ManualTimeZone = "Etc/UTC"; vm.AddManualLocation();
            Assert.Equal(0, vm.MaghribAdjustment);
            Assert.Equal(0, vm.SunriseAdjustment);
            vm.MaghribAdjustment = 3;
            var second = vm.SelectedSavedLocation;
            foreach (var language in vm.Languages)
            {
                vm.SelectedLanguage = language;
                vm.SelectedSavedLocation = first;
                Assert.Equal(5, vm.MaghribAdjustment);
                Assert.Equal(-2, vm.SunriseAdjustment);
                vm.SelectedSavedLocation = second;
                Assert.Equal(3, vm.MaghribAdjustment);
                Assert.Equal(0, vm.SunriseAdjustment);
            }
            // Re-select through different casing: identity must remain independent of display language.
            vm.SelectLocation(new(first with { Id = first.Id.ToUpperInvariant() }, first.City, ""));
            Assert.Equal(5, vm.MaghribAdjustment);
        }
        using var restarted = harness.Create();
        Assert.Equal(5, restarted.MaghribAdjustment);
        Assert.Equal(-2, restarted.SunriseAdjustment);
    });

    /// <summary>Imported settings rebind owned rows without changing local logging or startup authority.</summary>
    [Fact]
    public Task ConfirmedImportRebindsRowsAndCalculationsButRetainsDevicePrivacyChoices() => ui.Run(() =>
    {
        using var vm = harness.Create();
        vm.FajrAdjustment = 4;
        vm.LoggingEnabled = false;
        var original = harness.Settings.Value;
        var imported = new AppSettings
        {
            Language = "ar", LocationId = original.LocationId, SavedLocations = original.SavedLocations,
            StartOnLogin = true, LoggingEnabled = true, DebugLoggingEnabled = true,
            UseAutomaticConvention = false,
            MinuteAdjustments = new() { [Prayer.Fajr] = 9 },
            PrayerNotifications = Enum.GetValues<Prayer>().ToDictionary(prayer => prayer, _ => new PrayerNotificationSettings())
        };
        var calls = harness.Calculator.Calls;
        vm.ApplyImportedSettings(imported);
        Assert.True(harness.Calculator.Calls > calls);
        Assert.Equal(9, vm.FajrAdjustment);
        Assert.Equal(FlowDirection.RightToLeft, vm.FlowDirection);
        Assert.False(imported.StartOnLogin);
        Assert.False(imported.LoggingEnabled);
        Assert.False(imported.DebugLoggingEnabled);
        Assert.NotEmpty(vm.SettingsStatus);
        vm.NotificationRows[0].NotifyAfter = true;
        Assert.True(imported.PrayerNotifications[Prayer.Fajr].NotifyAfter);
        Assert.False(original.PrayerNotifications[Prayer.Fajr].NotifyAfter);
    });

    private static void ShowSettings(MainWindow window, int section)
    {
        window.FindControl<TabControl>("MainPages")!.SelectedIndex = 1;
        window.FindControl<TabControl>("SettingsPages")!.SelectedIndex = section;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    /// <summary>Checks shaped native tabs, RTL bounds and stable selections in every language/theme.</summary>
    [Fact]
    public Task NavigationTabsAreDistinctReadableAndInteractiveInEveryLanguage() => ui.Run(() =>
    {
        using var vm = harness.Create();
        var window = new MainWindow(vm, logger) { Width = 980, Height = 600, ForceClose = true };
        window.Show();
        try
        {
            var pages = window.FindControl<TabControl>("MainPages")!;
            var settings = window.FindControl<TabControl>("SettingsPages")!;
            ShowSettings(window, 2);
            var audio = window.FindControl<TabControl>("AudioPages")!;
            audio.SelectedIndex = 2;
            foreach (var language in vm.Languages)
            foreach (var dark in new[] { false, true })
            {
                vm.SelectedLanguage = language;
                vm.UseDarkMode = dark;
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                Assert.Equal(1, pages.SelectedIndex);
                Assert.Equal(2, settings.SelectedIndex);
                Assert.Equal(2, audio.SelectedIndex);
                AssertTabSurfaces(window, pages);
                AssertTabSurfaces(window, settings);
                AssertTabSurfaces(window, audio);

                if (string.Equals(language.Code, "en", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(language.Code, "ar", StringComparison.OrdinalIgnoreCase))
                {
                    ShowSettings(window, 0);
                    var directory = Path.Combine(AppContext.BaseDirectory, "ui-captures");
                    Directory.CreateDirectory(directory);
                    using var frame = window.CaptureRenderedFrame();
                    frame!.Save(Path.Combine(directory, $"tab-navigation-{language.Code}-{(dark ? "dark" : "light")}.png"),
                        PngBitmapEncoderOptions.Default);
                    ShowSettings(window, 2);
                }
            }

            // Real pointer/key input must continue using TabControl's built-in selection behavior.
            vm.SelectedLanguage = vm.Languages.Single(language => string.Equals(language.Code, "en", StringComparison.OrdinalIgnoreCase));
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            ClickTab(window, Assert.IsType<TabItem>(pages.Items[0]));
            Assert.Equal(0, pages.SelectedIndex);
            Assert.True(Assert.IsType<TabItem>(pages.Items[0]).Focus());
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Equal(1, pages.SelectedIndex);
            ClickTab(window, Assert.IsType<TabItem>(settings.Items[3]));
            Assert.Equal(3, settings.SelectedIndex);

            vm.SelectedLanguage = vm.Languages.Single(language => string.Equals(language.Code, "ar", StringComparison.OrdinalIgnoreCase));
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            ClickTab(window, Assert.IsType<TabItem>(settings.Items[0]));
            Assert.Equal(0, settings.SelectedIndex);
            Assert.True(Assert.IsType<TabItem>(settings.Items[0]).Focus());
            // Avalonia 12 WrapPanel advances logical child indices on Right, including RTL layouts.
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Equal(1, settings.SelectedIndex);
        }
        finally { window.Close(); }
    });

    private static void AssertTabSurfaces(MainWindow window, TabControl tabs)
    {
        var bounds = new List<Rect>();
        var fills = new List<Color>();
        foreach (var tab in tabs.Items.Cast<TabItem>())
        {
            Assert.True(tab.IsEffectivelyVisible);
            Assert.True(tab.Focusable);
            Assert.True(tab.FontSize >= 16);
            Assert.True(tab.Bounds.Height >= 48);
            Assert.True(tab.Bounds.Width >= 120);
            var surface = tab.GetVisualDescendants().OfType<Border>()
                .Single(border => string.Equals(border.Name, "PART_TabSurface", StringComparison.OrdinalIgnoreCase));
            Assert.True(surface.BorderThickness.Left >= 1 && surface.BorderThickness.Top >= 1
                && surface.BorderThickness.Right >= 1 && surface.BorderThickness.Bottom >= 1);
            Assert.True(surface.CornerRadius.TopLeft >= 8);
            Assert.True(Assert.IsAssignableFrom<ISolidColorBrush>(surface.BorderBrush).Color.A > 0);
            var fill = Assert.IsAssignableFrom<ISolidColorBrush>(surface.Background).Color;
            Assert.True(fill.A > 0);
            fills.Add(fill);
            var header = tab.GetVisualDescendants().OfType<TextBlock>()
                .Single(block => string.Equals(block.Text, tab.Header?.ToString(), StringComparison.OrdinalIgnoreCase));
            Assert.True(header.FontSize >= 16);
            Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(tab.Foreground).Color,
                Assert.IsAssignableFrom<ISolidColorBrush>(header.Foreground).Color);

            // The local origin is the physical right edge under RTL, so normalize both corners.
            var first = tab.TranslatePoint(default, window)!.Value;
            var last = tab.TranslatePoint(new Point(tab.Bounds.Width, tab.Bounds.Height), window)!.Value;
            var rectangle = new Rect(Math.Min(first.X, last.X), Math.Min(first.Y, last.Y),
                Math.Abs(last.X - first.X), Math.Abs(last.Y - first.Y));
            Assert.InRange(rectangle.Left, 0, window.ClientSize.Width);
            Assert.InRange(rectangle.Right, 0, window.ClientSize.Width);
            Assert.DoesNotContain(bounds, other => other.Intersects(rectangle));
            bounds.Add(rectangle);
        }
        var selectedFill = fills[tabs.SelectedIndex];
        Assert.All(fills.Where((_, index) => index != tabs.SelectedIndex), fill => Assert.NotEqual(selectedFill, fill));
    }

    private static void ClickTab(MainWindow window, TabItem tab)
    {
        var point = tab.TranslatePoint(new Point(tab.Bounds.Width / 2, tab.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }

    /// <summary>Renders every new navigation/transfer caption in all languages and both themes.</summary>
    [Fact]
    public Task MainAndSettingsPagesRemainReadableAndTranslatedInEveryLanguage() => ui.Run(() =>
    {
        using var vm = harness.Create();
        var window = new MainWindow(vm, logger) { Width = 980, Height = 600, ForceClose = true };
        window.Show();
        try
        {
            var pages = window.FindControl<TabControl>("MainPages")!;
            var settings = window.FindControl<TabControl>("SettingsPages")!;
            foreach (var language in vm.Languages)
            foreach (var dark in new[] { false, true })
            {
                vm.SelectedLanguage = language; vm.UseDarkMode = dark;
                pages.SelectedIndex = 0;
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                Assert.True(window.FindControl<ItemsControl>("TodayPrayerRows")!.IsEffectivelyVisible);
                Assert.False(window.FindControl<TextBlock>("SettingsStatusBanner")!.IsVisible);
                Assert.Equal(vm.TodayLabel, Assert.IsType<TabItem>(pages.Items[0]).Header);
                Assert.Equal(vm.SettingsLabel, Assert.IsType<TabItem>(pages.Items[1]).Header);
                string[] keys = ["Location", "PrayerClock", "AudioAdhkar", "General"];
                for (var section = 0; section < keys.Length; section++)
                {
                    ShowSettings(window, section);
                    Assert.Equal(vm[keys[section]], Assert.IsType<TabItem>(settings.Items[section]).Header);
                }
                var export = window.FindControl<Button>("ExportSettingsButton")!;
                var import = window.FindControl<Button>("ImportSettingsButton")!;
                Assert.Equal(vm["ExportSettings"], export.Content);
                Assert.Equal(vm["ImportSettings"], import.Content);
                Assert.True(import.FontSize >= 16);
                import.BringIntoView(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var first = import.TranslatePoint(default, window)!.Value;
                var last = import.TranslatePoint(new Point(import.Bounds.Width, import.Bounds.Height), window)!.Value;
                Assert.InRange(Math.Min(first.X, last.X), 0, window.Width);
                Assert.InRange(Math.Max(first.X, last.X), 0, window.Width);
                if (language.Code is "en" or "ar")
                {
                    var directory = Path.Combine(AppContext.BaseDirectory, "ui-captures");
                    Directory.CreateDirectory(directory);
                    using var general = window.CaptureRenderedFrame();
                    general!.Save(Path.Combine(directory, $"general-{language.Code}-{(dark ? "dark" : "light")}.png"), PngBitmapEncoderOptions.Default);
                    pages.SelectedIndex = 0;
                    Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                    using var today = window.CaptureRenderedFrame();
                    today!.Save(Path.Combine(directory, $"today-{language.Code}-{(dark ? "dark" : "light")}.png"), PngBitmapEncoderOptions.Default);
                }
            }
            window.Width = 1180; window.Height = 800;
            vm.SelectedLanguage = vm.Languages[0];
            pages.SelectedIndex = 0;
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var lastPrayer = window.FindControl<ItemsControl>("TodayPrayerRows")!.GetVisualDescendants().OfType<TextBlock>()
                .Single(block => string.Equals(block.Text, vm.PrayerRows[5].Time, StringComparison.OrdinalIgnoreCase));
            var bottom = lastPrayer.TranslatePoint(new Point(0, lastPrayer.Bounds.Height), window)!.Value.Y;
            Assert.InRange(bottom, 0, window.ClientSize.Height);
        }
        finally { window.Close(); }
    });

    /// <summary>Persistence failures remain visible on Today and never label a failed write as saved.</summary>
    [Fact]
    public Task StorageWarningsAreVisibleAndLocalizedOutsideSettings() => ui.Run(() =>
    {
        using var vm = harness.Create();
        var window = new MainWindow(vm, logger) { ForceClose = true };
        window.Show();
        try
        {
            vm.AutomaticSummerTime = false;
            vm.SummerTimeEnabled = true;
            vm.ApplySummerTimeChanges();
            harness.Settings.ReportStatus("SettingsSaveFailed");
            Assert.True(vm.CanApplySummerTime);
            Assert.True(window.FindControl<Button>("ApplySummerTimeButton")!.IsEnabled);
            foreach (var language in vm.Languages)
            {
                vm.SelectedLanguage = language;
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var banner = window.FindControl<TextBlock>("SettingsStatusBanner")!;
                Assert.True(banner.IsEffectivelyVisible);
                Assert.Equal(vm["SettingsSaveFailed"], banner.Text);
                Assert.Equal(vm["SettingsSaveFailed"], vm.SummerTimeEditStatus);
            }
            harness.Settings.ReportStatus("");
            Assert.False(vm.CanApplySummerTime);
            Assert.False(window.FindControl<Button>("ApplySummerTimeButton")!.IsEnabled);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task RepeatedTicks_ReuseSchedulesAndPrayerRows() => ui.Run(() =>
    {
        using var vm = harness.Create();
        vm.Tick();
        var rows = vm.PrayerRows;
        var calls = harness.Calculator.Calls;
        for (var second = 0; second < 120; second++)
        {
            harness.Clock.Now = harness.Clock.Now.AddSeconds(1);
            vm.Tick();
        }
        Assert.Same(rows, vm.PrayerRows);
        Assert.Equal(calls, harness.Calculator.Calls);
        vm.SelectedLanguage = vm.Languages.Single(language => language.Code == "ar");
        Assert.Equal(calls, harness.Calculator.Calls);
        vm.FajrAdjustment = 5;
        Assert.True(harness.Calculator.Calls > calls);
    });

    [Fact]
    public Task PrayerAudio_OverridesAdhkarAndOnlyFiresOnce() => ui.Run(() =>
    {
        harness.Clock.Now = new DateTimeOffset(2026, 9, 26, 11, 59, 0, TimeSpan.Zero);
        var settings = harness.Settings.Value;
        settings.PrayerNotifications[Prayer.Dhuhr].AudioEnabled = true;
        settings.PrayerNotifications[Prayer.Dhuhr].AudioPath = "dhuhr.ogg";
        settings.Adhkar = new() { Enabled = true, IntervalMinutes = 1, AudioFiles = ["adhkar.ogg"] };
        using var vm = harness.Create();
        var alerts = new List<PrayerAlert>();
        vm.PrayerDue += (_, alert) => alerts.Add(alert);
        vm.Tick();
        harness.Clock.Now = harness.Clock.Now.AddMinutes(1);
        vm.Tick(); vm.Tick();
        Assert.Single(alerts);
        Assert.Equal(PrayerAlertMoment.AtTime, alerts[0].Moment);
        Assert.Equal(["dhuhr.ogg"], harness.Audio.Played);
        settings.NotificationsEnabled = false;
        harness.Audio.IsBusy = false;
        harness.Clock.Now = harness.Clock.Now.AddHours(3);
        vm.Tick();
        Assert.Single(alerts);
    });

    [Fact]
    public Task AdjacentDayReminders_AreDeliveredWhenBrowsingOtherDates() => ui.Run(() =>
    {
        harness.Settings.Value.PrayerNotifications[Prayer.Fajr].MinutesBefore = 10;
        harness.Calculator.Fajr = new TimeOnly(0, 5);
        harness.Calculator.Isha = new TimeOnly(23, 55);
        harness.Settings.Value.PrayerNotifications[Prayer.Isha].NotifyAfter = true;
        harness.Settings.Value.PrayerNotifications[Prayer.Isha].MinutesAfter = 10;
        harness.Clock.Now = new DateTimeOffset(2026, 9, 26, 23, 55, 0, TimeSpan.Zero);
        using var vm = harness.Create();
        vm.NextDay(); vm.NextDay();
        var displayed = vm.DisplayDate;
        var alerts = new List<PrayerAlert>();
        vm.PrayerDue += (_, alert) => alerts.Add(alert);
        vm.Tick(); vm.Tick();
        Assert.Contains(alerts, alert => alert.Moment == PrayerAlertMoment.Before && alert.PrayerName == "Fajr");
        harness.Clock.Now = harness.Clock.Now.AddMinutes(10);
        vm.Tick(); vm.Tick();
        Assert.Single(alerts, alert => alert.Moment == PrayerAlertMoment.After && alert.PrayerName == "Isha");
        Assert.Equal(displayed, vm.DisplayDate);
    });

    /// <summary>Sunrise remains an optional audio event while prayer countdowns skip it across midnight.</summary>
    [Fact]
    public Task TodayAdvancesAtMidnightAndSunriseSupportsIndependentAudio() => ui.Run(() =>
    {
        harness.Clock.Now = new DateTimeOffset(2026, 9, 26, 23, 59, 59, TimeSpan.Zero);
        using var vm = harness.Create();
        var yesterday = vm.DisplayDate;
        harness.Clock.Now = harness.Clock.Now.AddSeconds(1);
        vm.Tick();
        Assert.NotEqual(yesterday, vm.DisplayDate);
        var sunrise = vm.NotificationRows.Single(row => row.Prayer == Prayer.Sunrise);
        sunrise.AudioEnabled = true; sunrise.AudioPath = "sunrise.ogg"; sunrise.NotifyAtTime = true;
        harness.Clock.Now = harness.Clock.Now.AddHours(6).AddSeconds(-1);
        vm.Tick();
        Assert.Equal(vm["Dhuhr"], vm.NextPrayerName);
        var sunriseTime = vm.PrayerRows.Single(row => row.Name == vm["Sunrise"]).Time;
        Assert.Empty(vm.PrayerRows.Single(row => row.Name == vm["Sunrise"]).Status);
        harness.Clock.Now = harness.Clock.Now.AddSeconds(1);
        vm.Tick();
        Assert.Equal(["sunrise.ogg"], harness.Audio.Played);
        Assert.Equal(vm["Dhuhr"], vm.NextPrayerName);
        Assert.Equal(sunriseTime, vm.PrayerRows.Single(row => row.Name == vm["Sunrise"]).Time);
        Assert.Equal(6, vm.PrayerRows.Count);
    });

    [Fact]
    public Task ManualLocationValidationDoesNotReplaceTheCurrentLocation() => ui.Run(() =>
    {
        using var vm = harness.Create();
        var original = vm.LocationName;
        vm.ManualName = "Invalid"; vm.ManualLatitude = 91; vm.ManualLongitude = 0;
        vm.AddManualLocation();
        Assert.Equal(original, vm.LocationName);
        Assert.NotEmpty(vm.LocationError);
    });

    // Await the owned worker to assert absence of late results without machine-dependent sleeps.
    private static Task SearchCompletion(MainViewModel viewModel) => (Task)typeof(MainViewModel)
        .GetField("_searchTask", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(viewModel)!;

    private sealed record SearchCall(string? Query, string Language)
    {
        public TaskCompletionSource<IReadOnlyList<LocationInfo>> Result { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class ControlledSearchRepository : ILocationRepository, IDisposable
    {
        private readonly Channel<SearchCall> _requests = Channel.CreateUnbounded<SearchCall>();
        private readonly List<SearchCall> _calls = [];
        private readonly object _gate = new();
        private int _active;
        public int Count => 0;
        public int CallCount { get { lock (_gate) return _calls.Count; } }
        public int MaximumActive { get; private set; }
        public LocationInfo Get(string id) => throw new InvalidOperationException("The selected location is in memory.");
        public Task<SearchCall> NextAsync() => _requests.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        public IReadOnlyList<LocationInfo> Search(string? query, string language, int limit = 30)
        {
            var call = new SearchCall(query, language);
            lock (_gate) { _calls.Add(call); MaximumActive = Math.Max(MaximumActive, ++_active); }
            _requests.Writer.TryWrite(call);
            try { return call.Result.Task.GetAwaiter().GetResult(); }
            finally { lock (_gate) _active--; }
        }

        public void Dispose()
        {
            // Release any blocked fake call if an assertion fails; never leave a test worker waiting.
            lock (_gate) foreach (var call in _calls) call.Result.TrySetResult([]);
        }
    }
}
