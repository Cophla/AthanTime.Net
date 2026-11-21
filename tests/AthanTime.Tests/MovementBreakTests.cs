using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AthanTime.Net;
using AthanTime.Net.Models;
using AthanTime.Net.Services;
using AthanTime.Net.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace AthanTime.Tests;

/// <summary>Checks exact movement deadlines, full post-reminder pauses and occupied-slot behavior without devices.</summary>
public sealed class MovementBreakSchedulerTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DeadlineIsNotEarlyAndExpiryStartsAFullPause()
    {
        var clock = new MovementBreakScheduler();
        var settings = new MovementBreakPreferences { Enabled = true, IntervalMinutes = 1, DisplaySeconds = 5 };
        string[] texts = ["first", "second"];
        Assert.False(clock.Tick(settings, texts, Start));
        Assert.False(clock.Tick(settings, texts, Start.AddSeconds(59.999)));
        Assert.True(clock.Tick(settings, texts, Start.AddMinutes(1)));
        Assert.Equal("first", clock.CurrentText);
        Assert.False(clock.Tick(settings, texts, Start.AddSeconds(64.999)));
        Assert.True(clock.Tick(settings, texts, Start.AddSeconds(65)));
        Assert.Null(clock.CurrentText);
        Assert.False(clock.Tick(settings, texts, Start.AddSeconds(124.999)));
        Assert.True(clock.Tick(settings, texts, Start.AddSeconds(125)));
        Assert.Equal("second", clock.CurrentText);
    }

    [Fact]
    public void BusyDeadlineIsSkippedWithoutConsumingAnIdeaOrQueuingCatchUp()
    {
        var clock = new MovementBreakScheduler();
        var settings = new MovementBreakPreferences { Enabled = true, IntervalMinutes = 1 };
        string[] texts = ["first", "second"];
        Assert.False(clock.Tick(settings, texts, Start));
        Assert.False(clock.Tick(settings, texts, Start.AddMinutes(1), blocked: true));
        Assert.Null(clock.CurrentText);
        Assert.False(clock.Tick(settings, texts, Start.AddSeconds(61)));
        Assert.True(clock.Tick(settings, texts, Start.AddMinutes(2)));
        Assert.Equal("first", clock.CurrentText);
        Assert.True(clock.Tick(settings, texts, Start.AddSeconds(121), blocked: true));
        Assert.Null(clock.CurrentText);
        Assert.False(clock.Tick(settings, texts, Start.AddSeconds(180)));
        Assert.True(clock.Tick(settings, texts, Start.AddSeconds(181)));
        Assert.Equal("second", clock.CurrentText);
    }

    [Fact]
    public void PreviewExpiresWhileDisabledAndPausedWithoutAdvancingRotation()
    {
        var clock = new MovementBreakScheduler();
        var settings = new MovementBreakPreferences { IntervalMinutes = 1, DisplaySeconds = 5 };
        string[] texts = ["first", "second"];
        clock.Preview("manual", 5, Start);
        Assert.True(clock.IsPreview);
        Assert.False(clock.Tick(settings, texts, Start.AddSeconds(4), paused: true));
        Assert.Equal("manual", clock.CurrentText);
        Assert.True(clock.Tick(settings, texts, Start.AddSeconds(5), paused: true));
        Assert.False(clock.IsPreview);
        Assert.Null(clock.CurrentText);
        settings.Enabled = true;
        Assert.False(clock.Tick(settings, texts, Start.AddSeconds(64)));
        Assert.True(clock.Tick(settings, texts, Start.AddSeconds(65)));
        Assert.Equal("first", clock.CurrentText);
    }

    [Fact]
    public void SnoozeWaitsExactlyFiveMinutesAndResetClearsPreview()
    {
        var clock = new MovementBreakScheduler();
        var settings = new MovementBreakPreferences { Enabled = true, IntervalMinutes = 1 };
        clock.Preview("manual", 30, Start);
        clock.Dismiss(Start, 5);
        Assert.False(clock.Tick(settings, ["first"], Start.AddSeconds(299.999)));
        Assert.True(clock.Tick(settings, ["first"], Start.AddMinutes(5)));
        clock.Preview("another", 30, Start.AddMinutes(5));
        clock.Reset();
        Assert.Null(clock.CurrentText);
        Assert.False(clock.IsPreview);
        Assert.False(clock.Tick(settings, ["first"], Start.AddMinutes(5)));
    }

    [Fact]
    public void EmptyDisabledOrPausedRemindersDoNotAppear()
    {
        var clock = new MovementBreakScheduler();
        var settings = new MovementBreakPreferences { Enabled = true, IntervalMinutes = 1 };
        Assert.False(clock.Tick(settings, [], Start));
        Assert.False(clock.Tick(settings, [], Start.AddMinutes(10)));
        Assert.False(clock.Tick(settings, ["first"], Start.AddMinutes(10), paused: true));
        settings.Enabled = false;
        Assert.False(clock.Tick(settings, ["first"], Start.AddMinutes(20)));
        settings.Enabled = true;
        Assert.False(clock.Tick(settings, ["first"], Start.AddMinutes(20)));
        Assert.True(clock.Tick(settings, ["first"], Start.AddMinutes(21)));
    }
}

/// <summary>Exercises movement editing, priority, clock gaps and rendered settings using isolated repositories.</summary>
[Collection("UI")]
public sealed class MovementBreakTests(UiTestHost ui, ViewModelHarness harness)
{
    [Fact]
    public Task OrdinaryTicksUseCachedStatementsAndNeverPollOrWriteTheDatabase() => ui.Run(() =>
    {
        harness.Settings.Value.NotificationsEnabled = false;
        var repository = EnabledRepository();
        using var vm = harness.Create(movementRepository: repository);
        var changes = new List<string?>();
        vm.MovementBreakChanged += (_, text) => changes.Add(text);
        vm.Tick();
        Advance(vm, 59);
        Assert.Empty(changes);
        Advance(vm, 1);
        Assert.Equal("Rest your hands.", changes.Last());
        Advance(vm, 5);
        Assert.Null(changes.Last());
        Advance(vm, 59);
        Assert.Null(changes.Last());
        Advance(vm, 1);
        Assert.Equal("Stand comfortably.", changes.Last());
        Advance(vm, 1000);
        Assert.Equal(1, repository.Loads);
        Assert.Equal(0, repository.Saves);
        Assert.Empty(harness.Audio.Played);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ClockJumpClearsStalePopupAndStartsANewInterval(bool backwards) => ui.Run(() =>
    {
        harness.Settings.Value.NotificationsEnabled = false;
        using var vm = harness.Create(movementRepository: EnabledRepository());
        string? current = null;
        vm.MovementBreakChanged += (_, text) => current = text;
        vm.Tick(); Advance(vm, 60);
        Assert.NotNull(current);
        harness.Clock.Now = harness.Clock.Now.AddMinutes(backwards ? -30 : 30);
        vm.Tick();
        Assert.Null(current);
        Advance(vm, 59);
        Assert.Null(current);
        Advance(vm, 1);
        Assert.Equal("Rest your hands.", current);
    });

    [Fact]
    public Task SilentPrayerPopupPreemptsMovementAndBlocksPreviewWithoutAudio() => ui.Run(() =>
    {
        harness.Settings.Value.NotificationsEnabled = false;
        using var vm = harness.Create(movementRepository: EnabledRepository());
        string? current = null;
        vm.MovementBreakChanged += (_, text) => current = text;
        vm.Tick(); Advance(vm, 60);
        Assert.NotNull(current);
        Assert.False(harness.Audio.IsBusy);
        vm.SetPrayerReminderVisible(true);
        Assert.Null(current);
        vm.TestMovementBreak();
        Assert.Null(current);
        Advance(vm, 60);
        Assert.Null(current);
        vm.SetPrayerReminderVisible(false);
        Advance(vm, 59);
        Assert.Null(current);
        Advance(vm, 1);
        Assert.Equal("Stand comfortably.", current);
    });

    [Fact]
    public Task SnoozeUsesFiveMinutesInsteadOfTheConfiguredOneMinutePause() => ui.Run(() =>
    {
        harness.Settings.Value.NotificationsEnabled = false;
        var repository = EnabledRepository();
        using var vm = harness.Create(movementRepository: repository);
        string? current = null;
        vm.MovementBreakChanged += (_, text) => current = text;
        vm.Tick(); Advance(vm, 60);
        Assert.Equal("Rest your hands.", current);
        vm.SnoozeMovementBreak();
        Assert.Null(current);
        Advance(vm, 299);
        Assert.Null(current);
        Advance(vm, 1);
        Assert.Equal("Stand comfortably.", current);
        Assert.Equal(1, vm.MovementIntervalMinutes);
        Assert.Equal(0, repository.Saves);
    });

    [Fact]
    public Task PreviewWorksWhenPausedAndDisabledAndExpiresWithoutSaving() => ui.Run(() =>
    {
        harness.Settings.Value.NotificationsEnabled = false;
        harness.Settings.Value.NotificationsPausedUntilUtc = harness.Clock.Now.AddHours(1);
        var repository = new MemoryMovementRepository();
        repository.Value.DisplaySeconds = 5;
        using var vm = harness.Create(movementRepository: repository);
        vm.SelectedMovementPractice = vm.MovementPractices[1];
        string? current = null;
        vm.MovementBreakChanged += (_, text) => current = text;
        vm.Tick(); vm.TestMovementBreak();
        Assert.Equal("Stand comfortably.", current);
        Assert.False(vm.MovementBreaksEnabled);
        Advance(vm, 4);
        Assert.NotNull(current);
        Advance(vm, 1);
        Assert.Null(current);
        Assert.Equal(0, repository.Saves);
        Assert.True(vm.NotificationsPaused);
    });

    [Fact]
    public Task PauseImportAndDisposalClearPreviewWithoutChangingMovementPreferences() => ui.Run(() =>
    {
        harness.Settings.Value.NotificationsEnabled = false;
        var repository = EnabledRepository();
        var vm = harness.Create(movementRepository: repository);
        string? current = null;
        vm.MovementBreakChanged += (_, text) => current = text;
        try
        {
            vm.TestMovementBreak(); Assert.NotNull(current);
            vm.ToggleNotificationsPause(); Assert.Null(current);
            vm.TestMovementBreak(); Assert.NotNull(current);
            Assert.True(vm.ApplySettingsBackup(new SettingsBackup(new AppSettings
            {
                LocationId = harness.Settings.Value.LocationId,
                SavedLocations = harness.Settings.Value.SavedLocations.ToList(),
                PrayerNotifications = harness.Settings.Value.PrayerNotifications.ToDictionary(pair => pair.Key, pair => pair.Value),
                UseAutomaticConvention = false, NotificationsEnabled = false, Language = "ar"
            }, null, "All")));
            Assert.Null(current);
            Assert.True(vm.MovementBreaksEnabled);
            Assert.Equal(2, vm.MovementPractices.Count);
            var expected = repository.Value.Practices[0].GetText("ar");
            Assert.Equal(expected, vm.MovementPractices[0].Label);
            vm.TestMovementBreak(); Assert.Equal(expected, current);
        }
        finally { vm.Dispose(); }
        Assert.Null(current);
        Assert.Equal(1, repository.Loads);
        Assert.Equal(0, repository.Saves);
    });

    [Fact]
    public Task CustomIdeasRetainTranslationsAndFallBackWithoutOnlineTranslation() => ui.Run(() =>
    {
        var repository = new MemoryMovementRepository();
        using var vm = harness.Create(movementRepository: repository);
        vm.MovementEditorLanguage = vm.Languages.Single(language => language.Code == "ar");
        vm.MovementTextDraft = "  امشِ قليلًا.  ";
        vm.AddMovementPractice();
        var id = vm.SelectedMovementPractice!.Value;
        var idea = repository.Value.Practices.Single(item => item.Id == id);
        Assert.Equal("امشِ قليلًا.", idea.GetText("en"));
        vm.MovementEditorLanguage = vm.Languages.Single(language => language.Code == "en");
        Assert.Equal("", vm.MovementTextDraft);
        vm.MovementTextDraft = "Walk a little.";
        vm.UpdateMovementPractice();
        idea = repository.Value.Practices.Single(item => item.Id == id);
        Assert.Equal("امشِ قليلًا.", idea.GetText("AR"));
        Assert.Equal("Walk a little.", idea.GetText("fr"));
        vm.SelectedLanguage = vm.Languages.Single(language => language.Code == "fr");
        Assert.Equal(id, vm.SelectedMovementPractice!.Value);
        Assert.Equal("Walk a little.", vm.SelectedMovementPractice.Label);
        vm.RemoveMovementPractice();
        Assert.DoesNotContain(repository.Value.Practices, item => item.Id == id);
        vm.ReloadMovementBreaks();
        Assert.Equal(2, vm.MovementPractices.Count);
        while (vm.MovementPractices.Count > 0)
        {
            vm.SelectedMovementPractice = vm.MovementPractices[0];
            vm.RemoveMovementPractice();
        }
        vm.ReloadMovementBreaks();
        Assert.Empty(vm.MovementPractices);
        Assert.Equal(vm["MovementEmpty"], vm.MovementStatus);
    });

    [Fact]
    public Task WholeBackupRestoresMovementAndKeepsLocalStartupAndLoggingChoices() => ui.Run(() =>
    {
        harness.Settings.Value.LoggingEnabled = false;
        var repository = new MemoryMovementRepository();
        using var vm = harness.Create(movementRepository: repository);
        vm.StartOnLogin = true;
        try
        {
            var imported = new AppSettings
            {
                LocationId = harness.Settings.Value.LocationId,
                SavedLocations = harness.Settings.Value.SavedLocations,
                PrayerNotifications = harness.Settings.Value.PrayerNotifications,
                Language = "ar", UseDarkMode = true
            };
            var movement = new MovementBreakPreferences { Enabled = true, IntervalMinutes = 12 };
            Assert.True(vm.ApplySettingsBackup(new SettingsBackup(imported, movement, "All")));
            Assert.True(vm.StartOnLogin);
            Assert.False(vm.LoggingEnabled);
            Assert.True(vm.UseDarkMode);
            Assert.Equal("ar", vm.SelectedLanguage.Code);
            Assert.True(vm.MovementBreaksEnabled);
            Assert.Equal(12, repository.Value.IntervalMinutes);
            Assert.Equal(1, repository.Saves);
        }
        finally { vm.StartOnLogin = false; }
    });

    [Fact]
    public Task MovementTransferStagesBeforeConfirmationThenReplacesOnlyMovementData() => ui.Run(() =>
    {
        var repository = new MemoryMovementRepository();
        using var vm = harness.Create(movementRepository: repository);
        Assert.True(vm.ExportMovementBreaks("movement-backup.json"));
        Assert.Equal(1, repository.Exports);
        Assert.Same(repository.Value, repository.ExportValue);

        var imported = new MovementBreakPreferences
        {
            Enabled = true,
            IntervalMinutes = 47,
            DisplaySeconds = 85,
            Practices = [new("imported", new Dictionary<string, string> { ["fr"] = "Texte original", ["ar"] = "النص العربي" })]
        };
        repository.ImportValue = imported;
        var staged = vm.ReadMovementBreaksImport("movement-import.json");
        Assert.Same(imported, staged);
        Assert.Equal(1, repository.Imports);
        Assert.Equal(0, repository.Saves);
        Assert.False(vm.MovementBreaksEnabled);
        Assert.Equal(2, vm.MovementPractices.Count);

        string? current = null;
        vm.MovementBreakChanged += (_, text) => current = text;
        vm.TestMovementBreak();
        Assert.NotNull(current);
        Assert.True(vm.ApplyImportedMovementBreaks(staged!));
        Assert.Null(current);
        Assert.Equal(1, repository.Saves);
        Assert.True(vm.MovementBreaksEnabled);
        Assert.Equal(47, vm.MovementIntervalMinutes);
        Assert.Equal(85, vm.MovementDisplaySeconds);
        Assert.Equal("Texte original", Assert.Single(vm.MovementPractices).Label);

        vm.Tick();
        Advance(vm, 47 * 60 - 1);
        Assert.Null(current);
        Advance(vm, 1);
        Assert.Equal("Texte original", current);
    });

    [Fact]
    public Task MovementTransferFailuresDoNotReplaceCurrentData() => ui.Run(() =>
    {
        var repository = new MemoryMovementRepository();
        using var vm = harness.Create(movementRepository: repository);
        var originalIdeas = vm.MovementPractices.Select(item => item.Value).ToArray();
        repository.Fail = true;

        Assert.False(vm.ExportMovementBreaks("blocked.json"));
        Assert.Null(vm.ReadMovementBreaksImport("invalid.json"));
        Assert.False(vm.ApplyImportedMovementBreaks(new MovementBreakPreferences
        {
            Enabled = true,
            Practices = [new("replacement", new Dictionary<string, string> { ["en"] = "Replacement" })]
        }));

        Assert.Equal(1, repository.Exports);
        Assert.Equal(1, repository.Imports);
        Assert.Equal(1, repository.Saves);
        Assert.False(repository.Value.Enabled);
        Assert.Equal(originalIdeas, repository.Value.Practices.Select(item => item.Id));
        Assert.Equal(originalIdeas, vm.MovementPractices.Select(item => item.Value));
        Assert.True(vm.MovementStorageFailed);
    });

    [Fact]
    public Task FailedInitialLoadCannotOverwriteSavedIdeasBeforeExplicitReload() => ui.Run(() =>
    {
        var repository = new MemoryMovementRepository { Fail = true };
        using var vm = harness.Create(movementRepository: repository);
        Assert.False(vm.MovementSettingsLoaded);
        Assert.Equal(1, repository.Loads);

        // Access recovering does not make the unknown on-disk contents safe to replace.
        repository.Fail = false;
        vm.MovementBreaksEnabled = true;
        vm.MovementIntervalMinutes = 12;
        vm.MovementTextDraft = "A newly entered idea.";
        vm.AddMovementPractice();
        Assert.False(vm.MovementSettingsLoaded);
        Assert.Equal(0, repository.Saves);
        Assert.Equal(1, repository.Loads);
        Assert.False(repository.Value.Enabled);
        Assert.Equal(30, repository.Value.IntervalMinutes);
        Assert.Equal(new[] { "first", "second" }, repository.Value.Practices.Select(idea => idea.Id));

        vm.ReloadMovementBreaks();
        Assert.True(vm.MovementSettingsLoaded);
        Assert.Equal(2, repository.Loads);
        Assert.Equal(2, vm.MovementPractices.Count);
        Assert.Equal("", vm.MovementStatus);
        vm.MovementBreaksEnabled = true;
        Assert.Equal(1, repository.Saves);
        Assert.True(repository.Value.Enabled);
    });

    [Fact]
    public Task StorageFailureIsVisibleBlocksPreviewsAndRequiresExplicitRecovery() => ui.Run(() =>
    {
        var repository = new MemoryMovementRepository { Fail = true };
        using var vm = harness.Create(movementRepository: repository);
        string? current = null;
        vm.MovementBreakChanged += (_, text) => current = text;
        Assert.True(vm.MovementStorageFailed);
        Assert.Equal(vm["MovementStorageError"], vm.MovementStatus);
        vm.TestMovementBreak(); Assert.Null(current);
        vm.Tick(); Advance(vm, 100);
        Assert.Equal(1, repository.Loads);
        repository.Fail = false;
        vm.ReloadMovementBreaks();
        Assert.False(vm.MovementStorageFailed);
        Assert.Equal(2, repository.Loads);
        vm.TestMovementBreak(); Assert.NotNull(current);
        repository.Fail = true;
        vm.MovementDisplaySeconds = 9;
        Assert.True(vm.MovementStorageFailed);
        Assert.Null(current);
        vm.TestMovementBreak(); Assert.Null(current);
        var saves = repository.Saves;
        Advance(vm, 100);
        Assert.Equal(saves, repository.Saves);
        Assert.Equal(2, repository.Loads);
        repository.Fail = false;
        vm.ReloadMovementBreaks();
        Assert.False(vm.MovementStorageFailed);
    });

    [Fact]
    public Task SettingsAndPopupRenderLocalizedControlsInEveryLanguageAndTheme() => ui.Run(() =>
    {
        var repository = new MemoryMovementRepository();
        repository.Value.IntervalMinutes = 13;
        repository.Value.DisplaySeconds = 17;
        using var vm = harness.Create(movementRepository: repository);
        var main = new MainWindow(vm, NullLogger<MainWindow>.Instance) { ForceClose = true };
        var expected = new Dictionary<string, (string Test, string Done, string Snooze)>
        {
            ["en"] = ("Test reminder", "Done", "Remind me in 5 minutes"),
            ["ar"] = ("تجربة التذكير", "تمّ", "ذكّرني بعد 5 دقائق"),
            ["fr"] = ("Tester le rappel", "Terminé", "Me rappeler dans 5 minutes"),
            ["es"] = ("Probar recordatorio", "Hecho", "Recordarme en 5 minutos"),
            ["tr"] = ("Hatırlatmayı dene", "Tamamlandı", "5 dakika sonra hatırlat"),
            ["de"] = ("Erinnerung testen", "Erledigt", "In 5 Minuten erinnern"),
            ["id"] = ("Uji pengingat", "Selesai", "Ingatkan dalam 5 menit"),
            ["ur"] = ("یاد دہانی آزمائیں", "مکمل", "5 منٹ بعد یاد دلائیں")
        };
        string? current = null;
        vm.MovementBreakChanged += (_, text) => current = text;
        try
        {
            main.Show();
            main.FindControl<TabControl>("MainPages")!.SelectedIndex = 1;
            main.FindControl<TabControl>("SettingsPages")!.SelectedIndex = 4;
            foreach (var language in vm.Languages)
            foreach (var dark in new[] { false, true })
            {
                vm.SelectedLanguage = language; vm.UseDarkMode = dark;
                Dispatcher.UIThread.RunJobs(); main.UpdateLayout();
                var test = main.FindControl<Button>("MovementTestButton")!;
                Assert.True(test.IsEffectivelyVisible);
                Assert.Equal(expected[language.Code].Test, test.Content);
                Assert.Equal(vm["MovementBreaksEnabled"], main.FindControl<CheckBox>("MovementEnabledCheckBox")!.Content);
                Assert.Equal((decimal?)13, main.FindControl<NumericUpDown>("MovementIntervalInput")!.Value);
                Assert.Equal((decimal?)17, main.FindControl<NumericUpDown>("MovementDurationInput")!.Value);
                Assert.Equal(FlowDirection.LeftToRight, main.FindControl<NumericUpDown>("MovementIntervalInput")!.FlowDirection);
                Assert.Equal(1000, main.FindControl<TextBox>("MovementInstructionInput")!.MaxLength);
                Assert.NotNull(main.FindControl<ListBox>("MovementPracticeSelector"));
                Assert.NotNull(main.FindControl<ComboBox>("MovementLanguageSelector"));
                // Editor direction follows the chosen instruction language, independently of UI direction.
                vm.MovementEditorLanguage = vm.Languages.Single(item => item.Code == (language.IsRightToLeft ? "en" : "ar"));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(language.IsRightToLeft ? FlowDirection.LeftToRight : FlowDirection.RightToLeft,
                    main.FindControl<TextBox>("MovementInstructionInput")!.FlowDirection);
                vm.MovementEditorLanguage = language;
                vm.SelectedMovementPractice = vm.MovementPractices[0];
                Dispatcher.UIThread.RunJobs(); main.UpdateLayout();
                Assert.Equal(language.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                    main.FindControl<TextBox>("MovementInstructionInput")!.FlowDirection);
                Capture(main, "settings", language.Code, dark);
                test.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(repository.Value.Practices[0].GetText(language.Code), current);
                var popup = new MovementBreakWindow(vm, "أرح يديك.");
                try
                {
                    popup.Show(main);
                    Dispatcher.UIThread.RunJobs(); popup.UpdateLayout();
                    Assert.False(popup.ShowActivated);
                    Assert.False(popup.ShowInTaskbar);
                    Assert.Equal(vm.SelectedTheme, popup.RequestedThemeVariant);
                    Assert.Equal(vm.FlowDirection, popup.FlowDirection);
                    var text = popup.GetVisualDescendants().OfType<TextBlock>().Single(item => item.Name == "MovementText");
                    Assert.Equal("أرح يديك.", text.Text);
                    Assert.Equal(FlowDirection.RightToLeft, text.FlowDirection);
                    var done = popup.GetVisualDescendants().OfType<Button>().Single(item => item.Name == "MovementDoneButton");
                    var snooze = popup.GetVisualDescendants().OfType<Button>().Single(item => item.Name == "MovementSnoozeButton");
                    Assert.Equal(expected[language.Code].Done, done.Content);
                    Assert.Equal(expected[language.Code].Snooze, snooze.Content);
                    Capture(popup, "popup", language.Code, dark);
                    (dark ? snooze : done).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.False(popup.IsVisible);
                    Assert.Null(current);
                }
                finally { popup.Close(); }
            }
        }
        finally { main.Close(); }
    });

    private static MemoryMovementRepository EnabledRepository()
    {
        var repository = new MemoryMovementRepository();
        repository.Value.Enabled = true;
        repository.Value.IntervalMinutes = 1;
        repository.Value.DisplaySeconds = 5;
        return repository;
    }

    private void Advance(MainViewModel vm, int seconds)
    {
        for (var second = 0; second < seconds; second++)
        {
            harness.Clock.Now = harness.Clock.Now.AddSeconds(1);
            vm.Tick();
        }
    }

    private static void Capture(Window window, string kind, string language, bool dark)
    {
        if (language is not ("en" or "ar")) return;
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        var directory = Path.Combine(AppContext.BaseDirectory, "ui-captures", "movement");
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, $"{kind}-{language}-{(dark ? "dark" : "light")}.png"), PngBitmapEncoderOptions.Default);
    }
}
