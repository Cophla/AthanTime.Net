using AthanTime.Net.Models;

namespace AthanTime.Net.Services;

/// <summary>A small interval clock: one recording per interval, never a catch-up queue after sleep.</summary>
public sealed class AdhkarScheduler(Random random)
{
    private readonly Random _random = random;
    private DateTimeOffset? _next;
    private int _index;

    /// <summary>Restarts the interval when playlist settings change, not when the UI language changes.</summary>
    public void Reset() { _next = null; _index = 0; }

    /// <summary>Returns a due file, skipping occupied intervals so Athan always has priority.</summary>
    public string? TakeDue(AdhkarSettings settings, DateTimeOffset now, bool audioBusy)
    {
        if (!settings.Enabled || settings.AudioFiles.Count == 0) { _next = null; return null; }
        var interval = TimeSpan.FromMinutes(Math.Clamp(settings.IntervalMinutes, 1, 1440));
        _next ??= now + interval;
        if (now < _next) return null;
        _next = now + interval;
        if (audioBusy) return null;
        var index = settings.RandomOrder ? _random.Next(settings.AudioFiles.Count) : _index++ % settings.AudioFiles.Count;
        _index %= settings.AudioFiles.Count;
        return settings.AudioFiles[index];
    }
}

/// <summary>A deterministic show–hide–pause clock driven by the app's existing one-second timer.</summary>
public sealed class TextAdhkarScheduler
{
    private DateTimeOffset? _next;
    private DateTimeOffset? _hideAt;
    private int _index;
    public string? CurrentText { get; private set; }
    /// <summary>Explicit previews can finish while automatic readings are disabled or paused.</summary>
    public bool IsPreview { get; private set; }

    /// <summary>Shows a manual reading without advancing the automatic text index.</summary>
    public void Preview(string text, TextAdhkarSettings settings, DateTimeOffset now)
    {
        CurrentText = text;
        IsPreview = true;
        _hideAt = now.AddSeconds(Math.Clamp(settings.DisplaySeconds, 5, 600));
    }

    /// <summary>Clears the current text and restarts at the beginning after a settings change.</summary>
    public void Reset() { CurrentText = null; _next = null; _hideAt = null; _index = 0; IsPreview = false; }

    /// <summary>Updates the visible text. Returns true only when a window must open or close.</summary>
    public bool Tick(TextAdhkarSettings settings, DateTimeOffset now, IReadOnlyList<string>? defaultTexts = null)
    {
        if (IsPreview)
        {
            if (now < _hideAt) return false;
            Dismiss(settings, now);
            return true;
        }
        var texts = settings.Texts.Count > 0 ? settings.Texts : defaultTexts ?? [];
        if (!settings.Enabled || texts.Count == 0)
        {
            var wasVisible = CurrentText is not null;
            Reset();
            return wasVisible;
        }
        if (CurrentText is not null)
        {
            if (now < _hideAt) return false;
            Dismiss(settings, now);
            return true;
        }
        _next ??= now.AddMinutes(Math.Clamp(settings.PauseMinutes, 1, 1440));
        if (now < _next) return false;
        CurrentText = texts[_index % texts.Count];
        _index = (_index + 1) % texts.Count;
        _hideAt = now.AddSeconds(Math.Clamp(settings.DisplaySeconds, 5, 600));
        return true;
    }

    /// <summary>Closes the current reading and starts a full pause, including after sleep or early dismissal.</summary>
    public void Dismiss(TextAdhkarSettings settings, DateTimeOffset now)
    {
        CurrentText = null;
        IsPreview = false;
        _hideAt = null;
        _next = now.AddMinutes(Math.Clamp(settings.PauseMinutes, 1, 1440));
    }
}
