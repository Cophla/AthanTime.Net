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
            main.FindControl<TabControl>("SettingsPages")!.SelectedIndex = 3;
            foreach (var language in vm.Languages)
            foreach (var dark in new[] { false, true })
            {
                vm.SelectedLanguage = language; vm.UseDarkMode = dark;
                Dispatcher.UIThread.RunJobs(); main.UpdateLayout();
                foreach (var caption in new[] { vm.HelpLabel, vm.AboutLabel })
                {
                    Click(main, caption);
                    var dialog = Assert.Single(main.OwnedWindows);
                    AssertAppearance(main, dialog, vm);
                    Assert.Equal(caption, dialog.Title);
                    Capture(dialog, caption == vm.HelpLabel ? "help" : "about", language.Code, dark);
                    Click(dialog, vm.OkLabel);
                    Assert.False(dialog.IsVisible);
                }
            }
        }
        finally { foreach (var child in main.OwnedWindows.ToArray()) child.Close(); main.Close(); }
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
