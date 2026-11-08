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
        Width = 460; Height = 320;
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
        stop.Margin = new Thickness(4); dismiss.Margin = new Thickness(4);
        buttons.Children.Add(stop); buttons.Children.Add(dismiss);
        SetBody(new StackPanel { Spacing = 12, Children = { heading, message, location, buttons } });
    }
}
