using AthanTime.Net.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace AthanTime.Net.ViewModels;

/// <summary>Edits per-user movement ideas in SQLite and caches only the current language for the app clock.</summary>
public sealed partial class MainViewModel
{
    private readonly IMovementBreakRepository _movementRepository;
    private readonly MovementBreakScheduler _movementScheduler = new();
    private MovementBreakPreferences _movementPreferences = new();
    private IReadOnlyList<string> _movementTexts = [];
    private IReadOnlyList<SelectOption<string>> _movementPractices = [];
    private SelectOption<string>? _selectedMovementPractice;
    private LanguageOption _movementEditorLanguage = LocalizationService.Languages[0];
    private string _movementTextDraft = "";
    private bool _movementStorageFailed;
    private bool _movementLoaded;
    private bool _movementPrayerVisible;
    private bool _movementPrayerRaisedThisTick;

    /// <summary>A non-null instruction opens the movement popup; null closes it.</summary>
    public event EventHandler<string?>? MovementBreakChanged;

    public IReadOnlyList<SelectOption<string>> MovementPractices => _movementPractices;
    public bool MovementStorageFailed => _movementStorageFailed;
    public bool MovementSettingsLoaded => _movementLoaded;
    public string MovementStatus => _movementStorageFailed ? T("MovementStorageError")
        : _movementPreferences.Practices.Count == 0 ? T("MovementEmpty") : "";
    public string MovementTextDraft { get => _movementTextDraft; set => Set(ref _movementTextDraft, value); }
    public SelectOption<string>? SelectedMovementPractice
    {
        get => _selectedMovementPractice;
        set { if (Set(ref _selectedMovementPractice, value)) LoadMovementDraft(); }
    }
    public LanguageOption MovementEditorLanguage
    {
        get => _movementEditorLanguage;
        set
        {
            if (value is null || !Set(ref _movementEditorLanguage, value)) return;
            OnPropertyChanged(nameof(MovementInstructionDirection));
            LoadMovementDraft();
        }
    }
    public Avalonia.Media.FlowDirection MovementInstructionDirection => MovementEditorLanguage.IsRightToLeft
        ? Avalonia.Media.FlowDirection.RightToLeft : Avalonia.Media.FlowDirection.LeftToRight;
    public bool MovementBreaksEnabled
    {
        get => _movementPreferences.Enabled;
        set { if (_movementLoaded && value != MovementBreaksEnabled) { _movementPreferences.Enabled = value; SaveMovementBreaks(); } }
    }
    public int MovementIntervalMinutes
    {
        get => _movementPreferences.IntervalMinutes;
        set
        {
            value = Math.Clamp(value, 1, 1440);
            if (_movementLoaded && value != MovementIntervalMinutes) { _movementPreferences.IntervalMinutes = value; SaveMovementBreaks(); }
        }
    }
    public int MovementDisplaySeconds
    {
        get => _movementPreferences.DisplaySeconds;
        set
        {
            value = Math.Clamp(value, 5, 600);
            if (_movementLoaded && value != MovementDisplaySeconds) { _movementPreferences.DisplaySeconds = value; SaveMovementBreaks(); }
        }
    }

    /// <summary>Explicitly reloads the saved database after a storage failure; never retries disk access on every tick.</summary>
    public void ReloadMovementBreaks()
    {
        try
        {
            _movementPreferences = _movementRepository.Load();
            _movementLoaded = true;
            _movementStorageFailed = false;
        }
        catch (Exception exception) when (MovementStorageException(exception))
        {
            _movementLoaded = false;
            _movementStorageFailed = true;
            _logger.LogWarning(exception, "Movement preferences could not be loaded");
        }
        ResetMovementBreaks();
        RefreshMovementTexts();
        if (_movementLoaded) LoadMovementDraft();
    }

    /// <summary>Exports the current movement configuration and all translations to a local JSON file.</summary>
    public bool ExportMovementBreaks(string path)
    {
        if (!_movementLoaded) return false;
        try
        {
            _movementRepository.Export(_movementPreferences, path);
            _logger.LogInformation("Exported movement reminders to {Path}", path);
            return true;
        }
        catch (Exception exception) when (MovementStorageException(exception))
        {
            _logger.LogWarning(exception, "Movement reminder export failed at {Path}", path);
            return false;
        }
    }

    /// <summary>Validates a movement JSON file without changing the active database or reminders.</summary>
    public MovementBreakPreferences? ReadMovementBreaksImport(string path)
    {
        try
        {
            var imported = _movementRepository.ReadImport(path);
            _logger.LogInformation("Validated movement reminder import from {Path}; it has not been applied", path);
            return imported;
        }
        catch (Exception exception) when (MovementStorageException(exception))
        {
            _logger.LogWarning(exception, "Movement reminder import was rejected at {Path}", path);
            return null;
        }
    }

    /// <summary>Replaces movement preferences only after the caller has confirmed a validated import.</summary>
    public bool ApplyImportedMovementBreaks(MovementBreakPreferences imported)
    {
        if (!_movementLoaded) return false;
        try
        {
            _movementRepository.Save(imported);
            _movementPreferences = imported;
            _movementStorageFailed = false;
            ResetMovementBreaks();
            RefreshMovementTexts();
            SelectedMovementPractice = MovementPractices.FirstOrDefault();
            return true;
        }
        catch (Exception exception) when (MovementStorageException(exception))
        {
            _movementStorageFailed = true;
            ResetMovementBreaks();
            RefreshMovementTexts();
            _logger.LogWarning(exception, "Imported movement reminders could not be saved");
            return false;
        }
    }

    /// <summary>Adds an original instruction in the selected editor language without online translation.</summary>
    public void AddMovementPractice()
    {
        if (!_movementLoaded) return;
        var text = MovementTextDraft.Trim();
        if (text.Length is < 1 or > 1000 || _movementPreferences.Practices.Count >= 100) return;
        var practice = new MovementPractice("custom-" + Guid.NewGuid().ToString("N"),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [MovementEditorLanguage.Code] = text });
        _movementPreferences.Practices.Add(practice);
        SaveMovementBreaks();
        SelectedMovementPractice = MovementPractices.First(item => item.Value == practice.Id);
    }

    /// <summary>Saves just the selected language of an idea, retaining its other translations.</summary>
    public void UpdateMovementPractice()
    {
        if (!_movementLoaded) return;
        var index = _movementPreferences.Practices.FindIndex(item => item.Id == SelectedMovementPractice?.Value);
        var text = MovementTextDraft.Trim();
        if (index < 0 || text.Length is < 1 or > 1000) return;
        var practice = _movementPreferences.Practices[index];
        var translations = new Dictionary<string, string>(practice.Texts, StringComparer.OrdinalIgnoreCase)
        { [MovementEditorLanguage.Code] = text };
        _movementPreferences.Practices[index] = new MovementPractice(practice.Id, translations);
        SaveMovementBreaks();
    }

    /// <summary>Removes one complete idea and its translations; an empty saved list stays empty.</summary>
    public void RemoveMovementPractice()
    {
        if (!_movementLoaded) return;
        if (SelectedMovementPractice is null) return;
        _movementPreferences.Practices.RemoveAll(item => item.Id == SelectedMovementPractice.Value);
        _selectedMovementPractice = null;
        MovementTextDraft = "";
        SaveMovementBreaks();
    }

    /// <summary>Previews an idea without enabling reminders, writing settings or advancing automatic rotation.</summary>
    public void TestMovementBreak()
    {
        if (_movementStorageFailed || MovementBusy) return;
        var text = _movementPreferences.Practices.FirstOrDefault(item => item.Id == SelectedMovementPractice?.Value)
            ?.GetText(_settings.Language) ?? _movementTexts.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(text)) return;
        _movementScheduler.Preview(text, _movementPreferences.DisplaySeconds, _timeProvider.GetUtcNow());
        MovementBreakChanged?.Invoke(this, text);
    }

    /// <summary>Closes the idea and starts the configured full pause.</summary>
    public void DismissMovementBreak()
    {
        _movementScheduler.Dismiss(_timeProvider.GetUtcNow(), MovementIntervalMinutes);
        MovementBreakChanged?.Invoke(this, null);
    }

    /// <summary>Closes the idea and postpones the next automatic reminder by five minutes.</summary>
    public void SnoozeMovementBreak()
    {
        _movementScheduler.Dismiss(_timeProvider.GetUtcNow(), 5);
        MovementBreakChanged?.Invoke(this, null);
    }

    /// <summary>Tracks actual silent prayer-popup visibility as well as audio priority.</summary>
    public void SetPrayerReminderVisible(bool visible)
    {
        _movementPrayerVisible = visible;
        if (visible) DismissMovementBreak();
    }

    private bool MovementBusy => _movementPrayerVisible || _movementPrayerRaisedThisTick
        || _audioPlayer.IsBusy || _textAdhkarScheduler.CurrentText is not null;

    private void TickMovementBreaks(DateTimeOffset now)
    {
        if (_movementScheduler.Tick(_movementPreferences, _movementTexts, now, MovementBusy,
            NotificationsPaused || _movementStorageFailed))
            MovementBreakChanged?.Invoke(this, _movementScheduler.CurrentText);
    }

    private void ResetMovementBreaks()
    {
        _movementScheduler.Reset();
        MovementBreakChanged?.Invoke(this, null);
    }

    private void LoadMovementDraft()
    {
        var practice = _movementPreferences.Practices.FirstOrDefault(item => item.Id == SelectedMovementPractice?.Value);
        // Show only the stored translation, never quietly overwrite a fallback in another language.
        MovementTextDraft = practice?.Texts.GetValueOrDefault(MovementEditorLanguage.Code) ?? "";
    }

    private void RefreshMovementTexts()
    {
        _movementTexts = _movementPreferences.Practices.Select(item => item.GetText(_settings.Language)).ToArray();
        var selectedId = _selectedMovementPractice?.Value;
        var previous = _movementPractices.ToDictionary(item => item.Value, StringComparer.OrdinalIgnoreCase);
        _movementPractices = _movementPreferences.Practices.Select(item =>
        {
            var option = previous.GetValueOrDefault(item.Id) ?? new SelectOption<string>(item.Id, "");
            option.Translate(item.GetText(_settings.Language));
            return option;
        }).ToArray();
        _selectedMovementPractice = _movementPractices.FirstOrDefault(item => item.Value == selectedId);
        OnPropertyChanged(nameof(MovementPractices));
        OnPropertyChanged(nameof(SelectedMovementPractice));
        OnPropertyChanged(nameof(MovementStatus));
        OnPropertyChanged(nameof(MovementStorageFailed));
        OnPropertyChanged(nameof(MovementSettingsLoaded));
        OnPropertyChanged(nameof(MovementBreaksEnabled));
        OnPropertyChanged(nameof(MovementIntervalMinutes));
        OnPropertyChanged(nameof(MovementDisplaySeconds));
    }

    private void SaveMovementBreaks()
    {
        // A failed initial load must never turn the fallback empty object into saved user data.
        if (!_movementLoaded) return;
        try
        {
            _movementRepository.Save(_movementPreferences);
            _movementStorageFailed = false;
        }
        catch (Exception exception) when (MovementStorageException(exception))
        {
            // Keep the visible edits retryable, but never run unsaved automatic reminders.
            _movementStorageFailed = true;
            _logger.LogWarning(exception, "Movement preferences could not be saved");
        }
        ResetMovementBreaks();
        RefreshMovementTexts();
    }

    private static bool MovementStorageException(Exception exception) => exception is IOException or InvalidDataException
        or UnauthorizedAccessException or SqliteException or ArgumentException or System.Text.Json.JsonException
        or System.Security.SecurityException;
}
