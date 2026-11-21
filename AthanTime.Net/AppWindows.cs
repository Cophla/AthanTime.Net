using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using AthanTime.Net.ViewModels;

namespace AthanTime.Net;

/// <summary>Shares the main window's live appearance, icon and card surface with secondary windows.</summary>
public class AppWindow : Window
{
    /// <summary>Connects a secondary window to the application's selected theme and reading direction.</summary>
    public AppWindow(MainViewModel viewModel)
    {
        DataContext = viewModel;
        this.Bind(RequestedThemeVariantProperty, new Binding(nameof(viewModel.SelectedTheme)));
        this.Bind(FlowDirectionProperty, new Binding(nameof(viewModel.FlowDirection)));
        Icon = App.CreateDesktopIcon();
    }

    internal void SetBody(Control body, bool scroll = true)
    {
        var card = new Border { Classes = { "card" }, Margin = new Thickness(12), Child = body };
        Content = scroll ? new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Content = card } : card;
    }
}

/// <summary>Summarizes the app with explicit browser links and the shared window appearance.</summary>
public sealed class AboutWindow : AppWindow
{
    /// <summary>Uses the owner's browser action so link failures follow the existing localized error path.</summary>
    public AboutWindow(MainViewModel viewModel, Func<string, Task> openLink) : base(viewModel)
    {
        this.Bind(TitleProperty, new Binding(nameof(viewModel.AboutLabel)));
        Width = 560;
        Height = 600;
        MinWidth = 360;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var summary = new TextBlock { Name = "AboutSummary", TextWrapping = TextWrapping.Wrap };
        summary.Bind(TextBlock.TextProperty, new Binding(nameof(viewModel.AboutText)));
        var panel = new StackPanel { Spacing = 14 };
        panel.Children.Add(summary);
        var version = new TextBlock { Name = "AboutVersion", FlowDirection = FlowDirection.LeftToRight };
        version.Bind(TextBlock.TextProperty, new Binding(nameof(viewModel.AppVersion)));
        panel.Children.Add(version);
        AddLink("AboutProjectLink", "ProjectPage", App.ProjectUrl);
        AddLink("AboutReleasesLink", "ReleasesPage", App.ReleasesUrl);
        var close = new Button { HorizontalAlignment = HorizontalAlignment.Right };
        close.Bind(ContentControl.ContentProperty, new Binding(nameof(viewModel.OkLabel)));
        close.Click += (_, _) => Close();
        panel.Children.Add(close);
        SetBody(panel);

        void AddLink(string name, string captionKey, string url)
        {
            var caption = new TextBlock { Classes = { "caption" } };
            caption.Bind(TextBlock.TextProperty, new Binding($"[{captionKey}]"));
            var link = new Button
            {
                Name = name, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = new TextBlock
                {
                    Text = url, TextWrapping = TextWrapping.Wrap, FlowDirection = FlowDirection.LeftToRight,
                    TextDecorations = new TextDecorationCollection
                    {
                        new TextDecoration { Location = TextDecorationLocation.Underline }
                    }
                }
            };
            link.Click += async (_, _) => await openLink(url);
            panel.Children.Add(caption);
            panel.Children.Add(link);
        }
    }
}

/// <summary>Displays prayer/sunrise reminder text with the main window's card, typography and buttons.</summary>
public sealed class PrayerNotificationWindow : AppWindow
{
    /// <summary>Builds a localized reminder without changing reminder dispatch or expiry.</summary>
    public PrayerNotificationWindow(MainViewModel viewModel, PrayerAlert alert) : base(viewModel)
    {
        Title = alert.Title;
        Width = 460;
        Height = 320;
        CanResize = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var heading = new TextBlock { Text = alert.Title, Classes = { "caption" } };
        var message = new TextBlock { Text = alert.Message, FontSize = 22, FontWeight = FontWeight.Bold };
        var location = new TextBlock { Text = $"{alert.Time} · {alert.LocationName}", Classes = { "caption" } };
        var stop = new Button();
        stop.Bind(ContentControl.ContentProperty, new Binding("[StopAudio]"));
        stop.Click += async (_, _) => await viewModel.StopAudioAsync();
        var dismiss = new Button { Content = alert.DismissLabel };
        dismiss.Click += (_, _) => Close();
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        stop.Margin = new Thickness(4);
        dismiss.Margin = new Thickness(4);
        buttons.Children.Add(stop);
        buttons.Children.Add(dismiss);
        SetBody(new StackPanel { Spacing = 12, Children = { heading, message, location, buttons } });
    }
}

/// <summary>A non-activating movement suggestion with the shared theme and app-clock lifetime.</summary>
public sealed class MovementBreakWindow : AppWindow
{
    /// <summary>Shows one original or user-written instruction with a reading accent.</summary>
    public MovementBreakWindow(MainViewModel viewModel, string text) : base(viewModel)
    {
        Width = 440; Height = 340; MinWidth = 260; MinHeight = 220;
        ShowActivated = false; ShowInTaskbar = false; Topmost = true;
        this.Bind(TitleProperty, new Binding("[MovementPopupTitle]"));
        var title = new TextBlock { Classes = { "section" } };
        title.Bind(TextBlock.TextProperty, new Binding("[MovementPopupTitle]"));
        var instruction = new ReadingHighlight(this, text, 22, "MovementText", viewModel.ReadingHighlightSpeed);
        var safety = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
        safety.Bind(TextBlock.TextProperty, new Binding("[MovementSafety]"));
        var done = new Button { Name = "MovementDoneButton", Margin = new Thickness(0, 4, 8, 4) };
        done.Bind(ContentControl.ContentProperty, new Binding("[MovementDismiss]"));
        done.Click += (_, _) => { viewModel.DismissMovementBreak(); Close(); };
        var snooze = new Button { Name = "MovementSnoozeButton", Margin = new Thickness(0, 4, 8, 4) };
        snooze.Bind(ContentControl.ContentProperty, new Binding("[MovementSnooze]"));
        snooze.Click += (_, _) => { viewModel.SnoozeMovementBreak(); Close(); };
        var content = new StackPanel { Spacing = 12, Children = { title, instruction, safety } };
        var panel = new Grid { RowDefinitions = RowDefinitions.Parse("*,Auto"), RowSpacing = 10 };
        panel.Children.Add(new ScrollViewer { Content = content });
        var buttons = new WrapPanel { Children = { done, snooze } };
        Grid.SetRow(buttons, 1); panel.Children.Add(buttons);
        SetBody(panel, scroll: false);
        Opened += (_, _) =>
        {
            if (Screens.ScreenFromWindow(this) is not { } screen) return;
            var area = screen.WorkingArea;
            var scale = DesktopScaling > 0 ? DesktopScaling : 1;
            // Leave room for native decorations; fit rather than force a minimum off-screen.
            var width = Math.Max(1, area.Width / scale - 40);
            var height = Math.Max(1, area.Height / scale - 80);
            MinWidth = Math.Min(MinWidth, width); MinHeight = Math.Min(MinHeight, height);
            Width = Math.Min(Width, width); Height = Math.Min(Height, height);
            Position = new PixelPoint(area.Right - (int)((Width + 20) * scale),
                area.Bottom - (int)((Height + 60) * scale));
        };
    }
}

/// <summary>A small non-activating reading window; the view-model timer owns its lifetime.</summary>
public sealed class TextAdhkarWindow : AppWindow
{
    /// <summary>Builds a scrollable reading view with live UI language/theme bindings.</summary>
    public TextAdhkarWindow(MainViewModel viewModel, string text) : base(viewModel)
    {
        Width = 400;
        Height = 280;
        MinWidth = 280;
        MinHeight = 220;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        this.Bind(TitleProperty, new Binding("[TextAdhkar]"));
        var dismiss = new Button { HorizontalAlignment = HorizontalAlignment.Right };
        dismiss.Bind(ContentControl.ContentProperty, new Binding(nameof(viewModel.OkLabel)));
        dismiss.Click += (_, _) => Close();
        var panel = new Grid { RowDefinitions = RowDefinitions.Parse("*,Auto"), RowSpacing = 12 };
        panel.Children.Add(new ScrollViewer
        {
            Content = new ReadingHighlight(this, text, 24, "AdhkarReadingText", viewModel.ReadingHighlightSpeed)
            { VerticalAlignment = VerticalAlignment.Center }
        });
        Grid.SetRow(dismiss, 1);
        panel.Children.Add(dismiss);
        SetBody(panel, scroll: false);
        Opened += (_, _) =>
        {
            if (Screens.Primary is not { } screen) return;
            var area = screen.WorkingArea;
            Position = new PixelPoint(area.Right - (int)((Width + 20) * screen.Scaling),
                area.Bottom - (int)((Height + 20) * screen.Scaling));
        };
    }

    /// <summary>Chooses paragraph direction from its first letter, independent of the UI language.</summary>
    public static FlowDirection ReadingDirection(string text)
    {
        var letter = text.FirstOrDefault(char.IsLetter);
        return letter is >= '\u0590' and <= '\u08ff' or >= '\ufb1d' and <= '\ufdff' or >= '\ufe70' and <= '\ufeff'
            ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }
}
