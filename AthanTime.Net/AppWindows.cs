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
            Content = new TextBlock
            {
                Text = text, FontSize = 24, TextWrapping = TextWrapping.Wrap,
                FlowDirection = ReadingDirection(text), VerticalAlignment = VerticalAlignment.Center
            }
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
