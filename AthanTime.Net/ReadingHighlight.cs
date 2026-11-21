using System.Globalization;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;

namespace AthanTime.Net;

/// <summary>Uses Avalonia's text selection foreground as a moving reading accent.</summary>
internal sealed class ReadingHighlight : SelectableTextBlock
{
    private readonly TextElementEnumerator _elements;
    private readonly DispatcherTimer _timer;

    public ReadingHighlight(Window window, string text, double size, string name, int charactersPerSecond)
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1d / charactersPerSecond) };
        Text = text;
        Name = name;
        FontSize = size;
        TextWrapping = TextWrapping.Wrap;
        FlowDirection = TextAdhkarWindow.ReadingDirection(text);
        Focusable = false;
        IsHitTestVisible = false;
        SelectionBrush = Brushes.Transparent;
        this.Bind(SelectionForegroundBrushProperty, new DynamicResourceExtension("AccentTextBrush"));
        _elements = StringInfo.GetTextElementEnumerator(text);

        window.Opened += (_, _) =>
        {
            _timer.Start();
            Advance();
        };
        window.Closed += (_, _) => _timer.Stop();
        _timer.Tick += (_, _) => Advance();
    }

    private void Advance()
    {
        while (_elements.MoveNext())
        {
            var start = _elements.ElementIndex;
            if (char.IsWhiteSpace(Text!, start)) continue;
            SelectionStart = start;
            SelectionEnd = start + StringInfo.GetNextTextElementLength(Text.AsSpan(start));
            return;
        }
        ClearSelection();
        _timer.Stop();
    }
}
