using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using AthanTime.Net;
using AthanTime.Net.Models;
using AthanTime.Net.Services;
using AthanTime.Net.ViewModels;

namespace AthanTime.Tests;

/// <summary>Verifies reading duration, hidden intervals, editing and RTL text independently of the clock's UI.</summary>
[Collection("UI")]
public sealed class TextAdhkarTests(UiTestHost ui, ViewModelHarness harness, TextAdhkarScheduler timer)
{
    [Fact]
    public void Scheduler_ShowsClosesWaitsAndAdvancesWithoutCatchup()
    {
        var now = new DateTimeOffset(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);
        var settings = new TextAdhkarSettings { Enabled = true, PauseMinutes = 1, DisplaySeconds = 10, Texts = ["first", "second"] };
        Assert.False(timer.Tick(settings, now));
        Assert.True(timer.Tick(settings, now.AddMinutes(1)));
        Assert.Equal("first", timer.CurrentText);
        Assert.False(timer.Tick(settings, now.AddSeconds(69)));
        Assert.True(timer.Tick(settings, now.AddSeconds(70)));
        Assert.Null(timer.CurrentText);
        Assert.False(timer.Tick(settings, now.AddSeconds(129)));
        Assert.True(timer.Tick(settings, now.AddSeconds(130)));
        Assert.Equal("second", timer.CurrentText);
        Assert.True(timer.Tick(settings, now.AddDays(1)));
        Assert.Null(timer.CurrentText);
        Assert.False(timer.Tick(settings, now.AddDays(1).AddSeconds(1)));
        Assert.True(timer.Tick(settings, now.AddDays(1).AddMinutes(1)));
        Assert.Equal("first", timer.CurrentText);
    }

    [Fact]
    public void DismissDisableAndEmptyListCloseAndRestartThePause()
    {
        var now = DateTimeOffset.UtcNow;
        var settings = new TextAdhkarSettings { Enabled = true, PauseMinutes = 1, DisplaySeconds = 30 };
        timer.Tick(settings, now);
        timer.Tick(settings, now.AddMinutes(1));
        timer.Dismiss(settings, now.AddMinutes(1));
        Assert.Null(timer.CurrentText);
        Assert.False(timer.Tick(settings, now.AddSeconds(61)));
        Assert.True(timer.Tick(settings, now.AddMinutes(2)));
        settings.Enabled = false;
        Assert.True(timer.Tick(settings, now.AddMinutes(2)));
        Assert.Null(timer.CurrentText);
        settings.Enabled = true;
        settings.Texts.Clear();
        Assert.False(timer.Tick(settings, now.AddDays(1)));
    }

    [Theory]
    [InlineData("سبحان الله", FlowDirection.RightToLeft)]
    [InlineData("123 — الحمد لله", FlowDirection.RightToLeft)]
    [InlineData("English text", FlowDirection.LeftToRight)]
    public void ReadingDirection_FollowsTextNotUiLanguage(string text, FlowDirection expected) =>
        Assert.Equal(expected, TextAdhkarWindow.ReadingDirection(text));

    [Fact]
    public Task EditingAndLanguageSwitchesPreserveTextAndTiming() => ui.Run(() =>
    {
        using var vm = harness.Create();
        vm.AdhkarTextDraft = "A custom reminder\nSecond line";
        vm.AddAdhkarText();
        vm.AdhkarTextDraft = "a CUSTOM reminder\nSecond line";
        vm.AddAdhkarText();
        Assert.Equal(4, vm.AdhkarTexts.Count);
        vm.SelectedAdhkarText = vm.AdhkarTexts[^1];
        vm.AdhkarTextDraft = "Changed reminder";
        vm.UpdateAdhkarText();
        Assert.Equal("Changed reminder", vm.SelectedAdhkarText);
        vm.TextAdhkarEnabled = true;
        vm.TextAdhkarPauseMinutes = 1;
        vm.TextAdhkarDisplaySeconds = 10;
        var changes = new List<string?>();
        vm.TextAdhkarChanged += (_, text) => changes.Add(text);
        vm.Tick();
        // Exercise normal ticking, not sleep recovery, which starts a fresh hidden interval.
        AdvanceSeconds(vm, 59);
        foreach (var language in vm.Languages) vm.SelectedLanguage = language;
        Assert.Empty(changes);
        AdvanceSeconds(vm, 1);
        Assert.Equal("سبحان الله", Assert.Single(changes));
        AdvanceSeconds(vm, 10);
        Assert.Null(changes[^1]);
        vm.RemoveAdhkarText();
        Assert.DoesNotContain("Changed reminder", vm.AdhkarTexts);
        Assert.Equal(3, vm.AdhkarTexts.Count);
    });

    [Fact]
    public Task SmallWindowDoesNotActivateAndFollowsThemeAndLanguage() => ui.Run(() =>
    {
        using var vm = harness.Create();
        var popup = new TextAdhkarWindow(vm, "سبحان الله");
        popup.Show();
        try
        {
            Assert.False(popup.ShowActivated);
            Assert.False(popup.ShowInTaskbar);
            Assert.Equal(400, popup.Width);
            vm.SelectedLanguage = vm.Languages.Single(language => language.Code == "ar");
            vm.UseDarkMode = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(FlowDirection.RightToLeft, popup.FlowDirection);
            Assert.Equal(vm.SelectedTheme, popup.RequestedThemeVariant);
            Assert.Equal(vm["TextAdhkar"], popup.Title);
        }
        finally { popup.Close(); }
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
