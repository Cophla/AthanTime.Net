namespace AthanTime.Net.Services;

/// <summary>A bounded, device-free movement reminder clock driven only by the existing application tick.</summary>
public sealed class MovementBreakScheduler
{
    private DateTimeOffset? _next;
    private DateTimeOffset? _hideAt;
    private int _index;
    public string? CurrentText { get; private set; }
    public bool IsPreview { get; private set; }

    /// <summary>Restarts intervals after settings changes, pauses, sleep or clock discontinuities.</summary>
    public void Reset() { _next = null; _hideAt = null; _index = 0; CurrentText = null; IsPreview = false; }

    /// <summary>Shows a test without consuming the next automatic idea.</summary>
    public void Preview(string text, int displaySeconds, DateTimeOffset now)
    {
        CurrentText = text;
        IsPreview = true;
        _hideAt = now.AddSeconds(Math.Clamp(displaySeconds, 5, 600));
    }

    /// <summary>Hides an instruction and waits the chosen number of minutes, preserving rotation.</summary>
    public void Dismiss(DateTimeOffset now, int minutes)
    {
        CurrentText = null;
        IsPreview = false;
        _hideAt = null;
        _next = now.AddMinutes(Math.Clamp(minutes, 1, 1440));
    }

    /// <summary>Returns true only for a popup change; occupied deadlines are skipped, never queued.</summary>
    public bool Tick(MovementBreakPreferences settings, IReadOnlyList<string> texts, DateTimeOffset now,
        bool blocked = false, bool paused = false)
    {
        if (CurrentText is not null)
        {
            if (!blocked && (IsPreview || (settings.Enabled && !paused)) && now < _hideAt) return false;
            Dismiss(now, settings.IntervalMinutes);
            return true;
        }
        if (!settings.Enabled || paused || texts.Count == 0) { _next = null; return false; }
        _next ??= now.AddMinutes(Math.Clamp(settings.IntervalMinutes, 1, 1440));
        if (now < _next) return false;
        _next = now.AddMinutes(Math.Clamp(settings.IntervalMinutes, 1, 1440));
        if (blocked) return false;
        CurrentText = texts[_index % texts.Count];
        _index = (_index + 1) % texts.Count;
        _hideAt = now.AddSeconds(Math.Clamp(settings.DisplaySeconds, 5, 600));
        return true;
    }
}
