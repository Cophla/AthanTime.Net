using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using AthanTime.Net.ViewModels;

namespace AthanTime.Net;

/// <summary>A small non-activating reading window; the view-model timer owns its lifetime.</summary>
public sealed class TextAdhkarWindow : AppWindow
{
    /// <summary>Builds a scrollable reading view with live UI language/theme bindings.</summary>
    public TextAdhkarWindow(MainViewModel viewModel, string text) : base(viewModel)
    {
        Width = 400; Height = 280; MinWidth = 280; MinHeight = 220;
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
