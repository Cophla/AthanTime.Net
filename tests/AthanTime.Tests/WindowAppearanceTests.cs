using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AthanTime.Net;
using AthanTime.Net.ViewModels;
using Microsoft.Extensions.Logging;

namespace AthanTime.Tests;

/// <summary>Checks real secondary windows against the main window's shared light/dark appearance.</summary>
[Collection("UI")]
public sealed class WindowAppearanceTests(UiTestHost ui, ViewModelHarness harness, ILogger<MainWindow> logger)
{
    [Fact]
    public Task CitySearchPlaceholderAndInputAreCenteredAndInset() => ui.Run(() =>
    {
        using var vm = harness.Create();
        var main = new MainWindow(vm, logger) { ForceClose = true };
        try
        {
            main.Show();
            main.FindControl<TabControl>("MainPages")!.SelectedIndex = 1;
            main.FindControl<TabControl>("SettingsPages")!.SelectedIndex = 1;
            var input = main.FindControl<TextBox>("LocationSearchInput")!;
            foreach (var language in vm.Languages)
            foreach (var dark in new[] { false, true })
            {
                vm.SelectedLanguage = language;
                vm.UseDarkMode = dark;
                input.Text = "";
                Dispatcher.UIThread.RunJobs();
                main.UpdateLayout();
                var placeholder = input.GetVisualDescendants().OfType<TextBlock>()
                    .Single(text => text.IsEffectivelyVisible && text.Text == vm.SearchPlaceLabel);
                Assert.Equal(vm.FlowDirection, input.FlowDirection);
                Assert.IsAssignableFrom<ISolidColorBrush>(input.PlaceholderForeground);
                AssertSearchTextBounds(input, placeholder);

                input.Text = "A";
                Dispatcher.UIThread.RunJobs();
                main.UpdateLayout();
                var presenter = input.GetVisualDescendants().OfType<TextPresenter>().Single();
                AssertSearchTextBounds(input, presenter);
            }
        }
        finally { main.Close(); }
    });

    private static void AssertSearchTextBounds(TextBox input, Control text)
    {
        var first = text.TranslatePoint(default, input)!.Value;
        var last = text.TranslatePoint(new Point(text.Bounds.Width, text.Bounds.Height), input)!.Value;
        Assert.InRange(Math.Abs((first.Y + last.Y) / 2 - input.Bounds.Height / 2), 0, 1);
        Assert.True(Math.Min(first.X, last.X) >= 10);
        Assert.True(input.Bounds.Width - Math.Max(first.X, last.X) >= 10);
    }

    [Fact]
    public Task CheckboxCaptionsAlignWithIndicatorsInEveryLanguageAndTheme() => ui.Run(() =>
    {
        using var vm = harness.Create();
        var main = new MainWindow(vm, logger) { ForceClose = true };
        try
        {
            main.Show();
            main.FindControl<TabControl>("MainPages")!.SelectedIndex = 1;
            main.FindControl<TabControl>("SettingsPages")!.SelectedIndex = 0;
            foreach (var language in vm.Languages)
            foreach (var dark in new[] { false, true })
            {
                vm.SelectedLanguage = language; vm.UseDarkMode = dark;
                Dispatcher.UIThread.RunJobs(); main.UpdateLayout();
                var checks = main.GetVisualDescendants().OfType<CheckBox>().Where(control => control.Bounds.Height > 0).ToArray();
                Assert.True(checks.Length >= 7);
                Capture(main, "checkboxes-general", language.Code, dark);
                foreach (var check in checks) AssertCheckboxAlignment(check);
                var wrapped = new CheckBox { Width = 200, IsThreeState = true, Content = string.Join(" ", Enumerable.Repeat(vm["StartOnLogin"], 4)) };
                var popup = new Window
                {
                    Width = 260, Height = 300, Content = wrapped,
                    FlowDirection = vm.FlowDirection, RequestedThemeVariant = vm.SelectedTheme
                };
                try
                {
                    popup.Show(main); Dispatcher.UIThread.RunJobs(); popup.UpdateLayout();
                    Assert.True(wrapped.Bounds.Height > wrapped.MinHeight);
                    foreach (var state in new bool?[] { false, true, null })
                    {
                        wrapped.IsChecked = state; Dispatcher.UIThread.RunJobs(); popup.UpdateLayout();
                        AssertCheckboxAlignment(wrapped);
                    }
                }
                finally { popup.Close(); }
            }
        }
        finally { main.Close(); }
    });

    private static void AssertCheckboxAlignment(CheckBox check)
    {
        var indicator = check.GetVisualDescendants().OfType<Border>().Single(control => control.Name == "NormalRectangle");
        var label = check.GetVisualDescendants().OfType<TextBlock>().Single(control => control.Text == check.Content as string);
        static Rect BoundsIn(Control control, Control parent)
        {
            var first = control.TranslatePoint(default, parent)!.Value;
            var last = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), parent)!.Value;
            return new Rect(new Point(Math.Min(first.X, last.X), Math.Min(first.Y, last.Y)),
                new Point(Math.Max(first.X, last.X), Math.Max(first.Y, last.Y)));
        }
        var mark = BoundsIn(indicator, check); var caption = BoundsIn(label, check);
        var row = (Control)indicator.GetVisualParent()!;
        Assert.True(Math.Abs(mark.Center.Y - caption.Center.Y) <= 1,
            $"'{check.Content}': indicator {mark}, caption {caption}, row {row.Bounds} ({row.VerticalAlignment}), checkbox {check.Bounds}");
        Assert.True(caption.Right <= mark.Left || mark.Right <= caption.Left,
            $"'{check.Content}': overlapping indicator {mark} and caption {caption}, direction {check.FlowDirection}");
    }

    [Fact]
    public Task FirstRunCityReviewIsTranslatedAndConfirmationHidesIt() => ui.Run(() =>
    {
        harness.Settings.Value.LocationNeedsConfirmation = true;
        using var vm = harness.Create();
        var window = new MainWindow(vm, logger) { ForceClose = true };
        try
        {
            window.Show();
            foreach (var language in vm.Languages)
            foreach (var dark in new[] { false, true })
            {
                vm.SelectedLanguage = language;
                vm.UseDarkMode = dark;
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var banner = window.FindControl<Grid>("FirstRunLocationBanner")!;
                Assert.True(banner.IsVisible);
                Assert.Contains(banner.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == vm["FirstRunLocationHelp"]);
                var button = banner.GetVisualDescendants().OfType<Button>().Single();
                Assert.Equal(vm["ConfirmCity"], button.Content);
                Assert.NotEqual("ConfirmCity", button.Content);
                Assert.True(button.Bounds.Width > 0);
            }
            window.FindControl<Grid>("FirstRunLocationBanner")!.GetVisualDescendants().OfType<Button>().Single()
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(vm.NeedsLocationConfirmation);
            Assert.False(window.FindControl<Grid>("FirstRunLocationBanner")!.IsVisible);
        }
        finally { window.Close(); }
    });

    /// <summary>Checks actual rendered button states and mouse/keyboard activation in both reading directions.</summary>
    [Fact]
    public Task ActionButtonsKeepTheSharedPaletteAcrossInteractionStates() => ui.Run(() =>
    {
        using var vm = harness.Create();
        foreach (var language in vm.Languages)
        foreach (var dark in new[] { false, true })
        {
            vm.SelectedLanguage = language; vm.UseDarkMode = dark;
            var alert = new PrayerAlert(PrayerAlertMoment.AtTime, vm["PrayerReminder"], vm["Dhuhr"], vm["Dhuhr"], "12:00", vm.LocationName, vm.OkLabel);
            var popup = new PrayerNotificationWindow(vm, alert);
            popup.Show();
            try
            {
                Dispatcher.UIThread.RunJobs(); popup.UpdateLayout();
                var button = popup.GetVisualDescendants().OfType<Button>().Single(control => Equals(control.Content, vm["StopAudio"]));
                var surface = button.GetVisualDescendants().OfType<ContentPresenter>().Single(control => control.Name == "PART_ContentPresenter");
                var label = button.GetVisualDescendants().OfType<TextBlock>().Single(control => control.Text == vm["StopAudio"]);
                var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), popup)!.Value;
                var clicks = 0;
                button.Click += (_, _) => clicks++;
                popup.MouseMove(new Point(1, 1));
                Dispatcher.UIThread.RunJobs();
                var normal = ColorOf(surface.Background);
                Assert.Equal(ColorOf(button.Background), normal);
                AssertReadable(surface, label);
                popup.MouseMove(point);
                Dispatcher.UIThread.RunJobs();
                var hover = ColorOf(surface.Background);
                Assert.NotEqual(normal, hover);
                AssertReadable(surface, label);
                Capture(popup, "buttons-hover", language.Code, dark);
                popup.MouseDown(point, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                Assert.True(button.IsPressed);
                Assert.NotEqual(hover, ColorOf(surface.Background));
                AssertReadable(surface, label);
                Capture(popup, "buttons-pressed", language.Code, dark);
                popup.MouseUp(point, MouseButton.Left);
                Assert.Equal(1, clicks);
                popup.MouseMove(new Point(1, 1));
                // Transfer focus first: focusing the already mouse-focused button retains pointer navigation.
                Assert.True(popup.GetVisualDescendants().OfType<Button>().Single(control => Equals(control.Content, vm.OkLabel)).Focus());
                Assert.True(button.Focus(NavigationMethod.Tab));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(new Thickness(2), surface.BorderThickness);
                AssertReadable(surface, label);
                Capture(popup, "buttons-focus", language.Code, dark);
                popup.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
                popup.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
                Assert.Equal(2, clicks);
                button.IsEnabled = false;
                Dispatcher.UIThread.RunJobs();
                Assert.NotEqual(normal, ColorOf(surface.Background));
                popup.MouseMove(point); popup.MouseDown(point, MouseButton.Left); popup.MouseUp(point, MouseButton.Left);
                Assert.Equal(2, clicks);
                Capture(popup, "buttons-disabled", language.Code, dark);
            }
            finally { popup.Close(); }
        }
    });

    [Fact]
    public Task RemindersAndExitUseMainAppearanceInEveryLanguageAndTheme() => ui.RunAsync(async () =>
    {
        using var vm = harness.Create();
        var main = new MainWindow(vm, logger) { ForceClose = true };
        main.Show();
        try
        {
            foreach (var language in vm.Languages)
            foreach (var dark in new[] { false, true })
            {
                vm.SelectedLanguage = language; vm.UseDarkMode = dark;
                var alert = new PrayerAlert(PrayerAlertMoment.Before, vm["PrayerReminder"],
                    string.Format(vm["BeforePrayerMessage"], vm["Dhuhr"], 15), vm["Dhuhr"], "12:00", vm.LocationName, vm.OkLabel);
                var prayer = new PrayerNotificationWindow(vm, alert);
                var reading = new TextAdhkarWindow(vm, "سبحان الله");
                prayer.Show(); reading.Show();
                try
                {
                    AssertAppearance(main, prayer, vm);
                    AssertAppearance(main, reading, vm);
                    Assert.False(reading.ShowActivated);
                    Assert.False(reading.ShowInTaskbar);
                    Assert.Contains(reading.GetVisualDescendants().OfType<TextBlock>(), text =>
                        text.Text == "سبحان الله" && text.FlowDirection == FlowDirection.RightToLeft);
                    vm.UseDarkMode = !dark;
                    AssertAppearance(main, prayer, vm);
                    AssertAppearance(main, reading, vm);
                    vm.UseDarkMode = dark;
                    Capture(prayer, "prayer", language.Code, dark);
                    Capture(reading, "reading", language.Code, dark);
                    var exit = main.RequestExitAsync();
                    var confirmation = Assert.Single(main.OwnedWindows);
                    AssertAppearance(main, confirmation, vm);
                    Capture(confirmation, "exit", language.Code, dark);
                    Click(confirmation, vm["Cancel"]);
                    await exit;
                    Assert.True(main.IsVisible);
                    Click(prayer, vm.OkLabel);
                    Click(reading, vm.OkLabel);
                    Assert.False(prayer.IsVisible);
                    Assert.False(reading.IsVisible);
                }
                finally { prayer.Close(); reading.Close(); }
            }
        }
        finally { foreach (var child in main.OwnedWindows.ToArray()) child.Close(); main.Close(); }
    });

    [Fact]
    public Task HelpAndAboutUseMainAppearanceInEveryLanguageAndTheme() => ui.Run(() =>
    {
        using var vm = harness.Create();
        var main = new MainWindow(vm, logger) { ForceClose = true };
        main.Show();
        try
        {
            main.FindControl<TabControl>("MainPages")!.SelectedIndex = 1;
            main.FindControl<TabControl>("SettingsPages")!.SelectedIndex = 0;
            Dispatcher.UIThread.RunJobs();
            foreach (var name in new[] { "ProjectPageButton", "ReleasesPageButton" })
                Assert.True(main.FindControl<Button>(name)!.IsEffectivelyVisible);
            foreach (var language in vm.Languages)
            foreach (var dark in new[] { false, true })
            {
                vm.SelectedLanguage = language; vm.UseDarkMode = dark;
                Dispatcher.UIThread.RunJobs(); main.UpdateLayout();
                foreach (var caption in new[] { vm.HelpLabel, vm.AboutLabel })
                {
                    Assert.Equal(vm["ProjectPage"], main.FindControl<Button>("ProjectPageButton")!.Content);
                    Assert.Equal(vm["ReleasesPage"], main.FindControl<Button>("ReleasesPageButton")!.Content);
                    Click(main, caption);
                    var dialog = Assert.Single(main.OwnedWindows);
                    AssertAppearance(main, dialog, vm);
                    Assert.Equal(caption, dialog.Title);
                    if (caption == vm.AboutLabel)
                    {
                        Assert.IsType<AboutWindow>(dialog);
                        var links = dialog.GetVisualDescendants().OfType<Button>()
                            .Where(button => button.Name is "AboutProjectLink" or "AboutReleasesLink").ToArray();
                        Assert.Equal(2, links.Length);
                        Assert.Equal(new[] { App.ProjectUrl, App.ReleasesUrl },
                            links.Select(link => Assert.IsType<TextBlock>(link.Content).Text));
                    }
                    Capture(dialog, caption == vm.HelpLabel ? "help" : "about", language.Code, dark);
                    Click(dialog, vm.OkLabel);
                    Assert.False(dialog.IsVisible);
                }
            }
        }
        finally { foreach (var child in main.OwnedWindows.ToArray()) child.Close(); main.Close(); }
    });

    [Fact]
    public Task AboutSummaryUpdatesWhileOpenAndLinksInvokeTheApprovedBrowserAction() => ui.Run(() =>
    {
        using var vm = harness.Create();
        var main = new MainWindow(vm, logger) { ForceClose = true };
        var opened = new List<string>();
        var about = new AboutWindow(vm, url => { opened.Add(url); return Task.CompletedTask; });
        try
        {
            main.Show();
            about.Show(main);
            Assert.Empty(opened);
            foreach (var language in vm.Languages)
            foreach (var dark in new[] { false, true })
            {
                vm.SelectedLanguage = language;
                vm.UseDarkMode = dark;
                Dispatcher.UIThread.RunJobs();
                AssertAppearance(main, about, vm);
                var summary = about.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "AboutSummary");
                Assert.Equal(vm.AboutText, summary.Text);
                var version = about.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "AboutVersion");
                Assert.Equal(vm.AppVersion, version.Text);
                Assert.Equal(FlowDirection.LeftToRight, version.FlowDirection);
                Assert.Equal(vm.AboutLabel, about.Title);
            }
            foreach (var name in new[] { "AboutProjectLink", "AboutReleasesLink" })
            {
                var link = about.GetVisualDescendants().OfType<Button>().Single(button => button.Name == name);
                var label = Assert.IsType<TextBlock>(link.Content);
                Assert.Equal(FlowDirection.LeftToRight, label.FlowDirection);
                Assert.Equal(TextDecorationLocation.Underline, Assert.Single(label.TextDecorations!).Location);
                link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            Assert.Equal(new[] { App.ProjectUrl, App.ReleasesUrl }, opened);
        }
        finally { about.Close(); main.Close(); }
    });

    private static void AssertAppearance(MainWindow main, Window secondary, MainViewModel vm)
    {
        Dispatcher.UIThread.RunJobs(); main.UpdateLayout(); secondary.UpdateLayout();
        Assert.Equal(vm.SelectedTheme, secondary.RequestedThemeVariant);
        Assert.Equal(main.FlowDirection, secondary.FlowDirection);
        Assert.Equal(ColorOf(main.Background), ColorOf(secondary.Background));
        Assert.Equal(main.FontFamily, secondary.FontFamily);
        Assert.NotNull(secondary.Icon);
        var card = Assert.Single(secondary.GetVisualDescendants().OfType<Border>(), border => border.Classes.Contains("card"));
        var mainCard = main.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("card")
            && ColorOf(border.Background) != Color.Parse("#1B5944"));
        Assert.Equal(ColorOf(mainCard.Background), ColorOf(card.Background));
        Assert.Equal(mainCard.CornerRadius, card.CornerRadius);
        Assert.Equal(mainCard.Padding, card.Padding);
        var bodyText = secondary.GetVisualDescendants().OfType<TextBlock>().First(text => !text.Classes.Contains("caption"));
        Assert.Equal(ColorOf(main.Foreground), ColorOf(bodyText.Foreground));
        Assert.Equal(main.FontFamily, bodyText.FontFamily);
        // Scrollbar repeat buttons retain their compact native template; check dialog actions.
        var buttons = secondary.GetVisualDescendants().OfType<Button>().Where(button => button.GetType() == typeof(Button)).ToArray();
        var mainButton = main.GetVisualDescendants().OfType<Button>().First(button => button.GetType() == typeof(Button) && !button.Classes.Contains("location"));
        Assert.NotEmpty(buttons);
        Assert.All(buttons, button =>
        {
            Assert.True(button.MinHeight >= 44);
            Assert.Equal(16, button.FontSize);
            Assert.True(button.Bounds.Height >= 44);
            Assert.Equal(mainButton.CornerRadius, button.CornerRadius);
            Assert.Equal(mainButton.BorderThickness, button.BorderThickness);
            Assert.Equal(ColorOf(mainButton.Background), ColorOf(button.Background));
            Assert.Equal(ColorOf(mainButton.Foreground), ColorOf(button.Foreground));
            Assert.Equal(ColorOf(mainButton.BorderBrush), ColorOf(button.BorderBrush));
            Assert.Equal(mainButton.FontWeight, button.FontWeight);
        });
    }

    private static void AssertReadable(ContentPresenter surface, TextBlock label)
    {
        var foreground = ColorOf(surface.Foreground);
        var background = ColorOf(surface.Background);
        Assert.Equal(foreground, ColorOf(label.Foreground));
        static double Luminance(Color color)
        {
            static double Linear(byte value) => value / 255d <= 0.04045 ? value / 255d / 12.92 : Math.Pow((value / 255d + 0.055) / 1.055, 2.4);
            return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        }
        var first = Luminance(foreground); var second = Luminance(background);
        Assert.True((Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05) >= 4.5);
    }

    private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static void Click(Window window, string caption) => window.GetVisualDescendants().OfType<Button>()
        .Single(button => Equals(button.Content, caption)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Capture(Window window, string kind, string language, bool dark)
    {
        if (language is not ("en" or "ar")) return;
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        var directory = Path.Combine(AppContext.BaseDirectory, "ui-captures", "popups");
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, $"{kind}-{language}-{(dark ? "dark" : "light")}.png"), PngBitmapEncoderOptions.Default);
    }
}
