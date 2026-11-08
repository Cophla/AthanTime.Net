using AthanTime.Net.Services;
using Microsoft.Extensions.Logging;

namespace AthanTime.Net.ViewModels;

/// <summary>JSON-backed text Adhkar editing and timed popup state.</summary>
public sealed partial class MainViewModel
{
    private readonly TextAdhkarScheduler _textAdhkarScheduler;
    private string _adhkarTextDraft = "";
    private string? _selectedAdhkarText;

    /// <summary>A non-null text opens the small reading window; null closes it.</summary>
    public event EventHandler<string?>? TextAdhkarChanged;

    public IReadOnlyList<string> AdhkarTexts => _settings.TextAdhkar.Texts;
    public string AdhkarTextDraft { get => _adhkarTextDraft; set => Set(ref _adhkarTextDraft, value); }
    public string? SelectedAdhkarText
    {
        get => _selectedAdhkarText;
        set
        {
            if (!Set(ref _selectedAdhkarText, value)) return;
            if (value is not null) AdhkarTextDraft = value;
        }
    }
    public bool TextAdhkarEnabled
    {
        get => _settings.TextAdhkar.Enabled;
        set
        {
            if (value == TextAdhkarEnabled) return;
            _settings.TextAdhkar.Enabled = value;
            SaveTextAdhkar();
            _logger.LogInformation("Automatic text Adhkar {State}", value ? "enabled" : "disabled");
        }
    }
    public int TextAdhkarDisplaySeconds
    {
        get => _settings.TextAdhkar.DisplaySeconds;
        set { value = Math.Clamp(value, 5, 600); if (value == TextAdhkarDisplaySeconds) return; _settings.TextAdhkar.DisplaySeconds = value; SaveTextAdhkar(); }
    }
    public int TextAdhkarPauseMinutes
    {
        get => _settings.TextAdhkar.PauseMinutes;
        set { value = Math.Clamp(value, 1, 1440); if (value == TextAdhkarPauseMinutes) return; _settings.TextAdhkar.PauseMinutes = value; SaveTextAdhkar(); }
    }

    /// <summary>Adds one complete text; line breaks are preserved and comparisons ignore case.</summary>
    public void AddAdhkarText()
    {
        var text = AdhkarTextDraft.Trim();
        if (text.Length == 0 || _settings.TextAdhkar.Texts.Contains(text, StringComparer.OrdinalIgnoreCase)) return;
        _settings.TextAdhkar.Texts = [.. _settings.TextAdhkar.Texts, text];
        AdhkarTextDraft = "";
        SaveTextAdhkar();
    }

    /// <summary>Replaces the selected text while preserving list order.</summary>
    public void UpdateAdhkarText()
    {
        var text = AdhkarTextDraft.Trim();
        var index = _settings.TextAdhkar.Texts.FindIndex(item => item.Equals(SelectedAdhkarText, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || text.Length == 0) return;
        if (_settings.TextAdhkar.Texts.Where((_, i) => i != index).Contains(text, StringComparer.OrdinalIgnoreCase)) return;
        _settings.TextAdhkar.Texts[index] = text;
        _selectedAdhkarText = text;
        SaveTextAdhkar();
    }

    /// <summary>Removes just the selected text from the JSON list.</summary>
    public void RemoveAdhkarText()
    {
        _settings.TextAdhkar.Texts.RemoveAll(text => text.Equals(SelectedAdhkarText, StringComparison.OrdinalIgnoreCase));
        SelectedAdhkarText = null;
        AdhkarTextDraft = "";
        SaveTextAdhkar();
    }

    /// <summary>Dismisses a reading early and waits the configured pause before continuing.</summary>
    public void DismissTextAdhkar()
    {
        _textAdhkarScheduler.Dismiss(_settings.TextAdhkar, _timeProvider.GetUtcNow());
        TextAdhkarChanged?.Invoke(this, null);
    }

    private void SaveTextAdhkar()
    {
        _textAdhkarScheduler.Reset();
        TextAdhkarChanged?.Invoke(this, null);
        _settings.TextAdhkar.Texts = _settings.TextAdhkar.Texts.ToList();
        _settingsService.Save(_settings);
        OnPropertyChanged(nameof(AdhkarTexts));
        OnPropertyChanged(nameof(SelectedAdhkarText));
        OnPropertyChanged(nameof(TextAdhkarEnabled));
        OnPropertyChanged(nameof(TextAdhkarDisplaySeconds));
        OnPropertyChanged(nameof(TextAdhkarPauseMinutes));
        _logger.LogDebug("Saved text Adhkar preferences: {Count} texts", AdhkarTexts.Count);
    }
}
