using System.Collections.ObjectModel;
using Avalonia.Threading;
using AthanTime.Net.Models;
using AthanTime.Net.Services;
using Microsoft.Extensions.Logging;

namespace AthanTime.Net.ViewModels;

/// <summary>Manual-location and local-audio settings, separated from the prayer clock for readability.</summary>
public sealed partial class MainViewModel
{
    private readonly ManualLocationService _manualLocations;
    private readonly IAudioPlayer _audioPlayer;
    private readonly AdhkarScheduler _adhkarScheduler;
    private DispatcherTimer? _searchTimer;
    private string _manualName = "";
    private string _manualCountry = "";
    private string _manualTimeZone = "Etc/UTC";
    private decimal? _manualLatitude;
    private decimal? _manualLongitude;
    private string _locationErrorKey = "";
    private string _audioStatusKey = "AudioStopped";
    private LocationInfo? _selectedSavedLocation;
    private string? _selectedAdhkarFile;
    private int _summerYear;
    private string _summerZoneId = "";
    private IReadOnlyList<SummerTimeTransition> _summerTransitions = [];
    private SummerTimeSettings _summerDraft = new();
    private string _summerDraftZone = "";
    private readonly IReadOnlyList<SelectOption<SummerTimeMode>> _summerModes = Enum.GetValues<SummerTimeMode>()
        .Select(mode => new SelectOption<SummerTimeMode>(mode, "")).ToArray();

    /// <summary>Stable mode choices, translated without replacing the selection.</summary>
    public IReadOnlyList<SelectOption<SummerTimeMode>> SummerTimeModes => _summerModes;
    /// <summary>Uses automatic rules by default, a custom annual schedule, or an explicitly forced state.</summary>
    public SelectOption<SummerTimeMode> SelectedSummerTimeMode
    {
        get => _summerModes.Single(item => item.Value == (AutomaticSummerTime ? SummerTimeMode.Automatic
            : SummerDraft.UseCustomSchedule ? SummerTimeMode.CustomDates : SummerTimeMode.Manual));
        set
        {
            if (value is null || value.Value == SelectedSummerTimeMode.Value) return;
            if (AutomaticSummerTime && value.Value == SummerTimeMode.Manual)
            {
                var minutes = AutomaticSummerMinutes;
                SummerDraft.Enabled = minutes != 0;
                if (minutes != 0) SummerDraft.AdjustmentMinutes = minutes;
            }
            SummerDraft.Automatic = value.Value == SummerTimeMode.Automatic;
            SummerDraft.UseCustomSchedule = value.Value == SummerTimeMode.CustomDates;
            SummerDraftChanged();
        }
    }

    /// <summary>Reusable start rule; changing language or city preserves this editor and its choices.</summary>
    public SummerTimeRuleEditor SummerTimeStartEditor { get; }
    /// <summary>Reusable end rule, interpreted using the clock before ending the seasonal adjustment.</summary>
    public SummerTimeRuleEditor SummerTimeEndEditor { get; }
    /// <summary>Only custom-date mode shows annual rule editors.</summary>
    public bool CustomSummerTime => !AutomaticSummerTime && SummerDraft.UseCustomSchedule;
    /// <summary>Only manual on/off mode permits forcing the current active checkbox.</summary>
    public bool ForceSummerTime => ManualSummerTime && !CustomSummerTime;
    /// <summary>Invalid draft seasons cannot replace the active rules or silently fall back to automatic.</summary>
    public string SummerTimeError => CustomSummerTime && !_timeZones.IsCustomScheduleValid(_selectedDate.Year, _timeZone, SummerDraft)
        ? T("SummerInvalidDates") : "";

    /// <summary>Indicates that the preview differs from the active clock preference.</summary>
    public bool SummerTimeHasChanges => !SameSummerTime(SummerDraft, SummerTime);
    /// <summary>Invalid drafts cannot replace the active clock rules.</summary>
    public bool CanApplySummerTime => (SummerTimeHasChanges
        || _settingsService.StatusKey.Equals("SettingsSaveFailed", StringComparison.OrdinalIgnoreCase)) && SummerTimeError.Length == 0;
    /// <summary>Actual active clock status, unaffected by unapplied edits.</summary>
    public bool ActiveSummerTimeEnabled => EffectiveSummerMinutes != 0;
    /// <summary>Separates draft preview from the current prayer clock.</summary>
    public string SummerTimeEditStatus => T(SummerTimeHasChanges ? "SummerDraftPending"
        : _settingsService.StatusKey.Equals("SettingsSaveFailed", StringComparison.OrdinalIgnoreCase) ? "SettingsSaveFailed" : "SummerDraftSaved");
    /// <summary>Shows the committed seasonal contribution separately from the editor preview.</summary>
    public string ActiveSummerTimeDescription => string.Format(Culture(), T("SummerActiveClock"), EffectiveSummerMinutes);

    /// <summary>Commits a valid preview or retries a failed save, then refreshes the prayer display.</summary>
    public void ApplySummerTimeChanges()
    {
        if (!CanApplySummerTime) return;
        var oldToday = DateOnly.FromDateTime(LocationNow());
        _settings.SummerTimeOverrides[_timeZone.Id] = CopySummerTime(SummerDraft);
        SaveSummerTime(oldToday);
    }

    /// <summary>Discards uncommitted edits and restores the active preference.</summary>
    public void CancelSummerTimeChanges()
    {
        _summerDraftZone = "";
        RefreshSummerTimeEditors();
        RaiseAll();
    }

    /// <summary>Previews automatic offline rules; Apply is still required.</summary>
    public void RestoreAutomaticSummerTime() => SelectedSummerTimeMode = _summerModes[0];

    private void RefreshSummerTimeEditors()
    {
        _ = SummerDraft;
        foreach (var option in _summerModes) option.Translate(T("SummerMode" + option.Value));
        SummerTimeStartEditor.Refresh(Culture());
        SummerTimeEndEditor.Refresh(Culture());
    }

    private void ChangeSummerRule(Action assign)
    {
        assign();
        SummerDraftChanged();
    }

    private void SummerDraftChanged()
    {
        RefreshSummerTimeEditors();
        RaiseAll();
    }

    private SummerTimeSettings SummerDraft
    {
        get
        {
            if (!_summerDraftZone.Equals(_timeZone.Id, StringComparison.OrdinalIgnoreCase))
            {
                _summerDraft = CopySummerTime(SummerTime);
                _summerDraftZone = _timeZone.Id;
            }
            return _summerDraft;
        }
    }

    private static SummerTimeSettings CopySummerTime(SummerTimeSettings value) => new()
    {
        Automatic = value.Automatic, Enabled = value.Enabled, AdjustmentMinutes = value.AdjustmentMinutes,
        UseCustomSchedule = value.UseCustomSchedule, Start = CopySummerRule(value.Start), End = CopySummerRule(value.End)
    };

    private static SummerTimeRule CopySummerRule(SummerTimeRule value) => new()
    {
        Month = value.Month, UseWeekday = value.UseWeekday, Day = value.Day, Occurrence = value.Occurrence,
        Weekday = value.Weekday, Hour = value.Hour, Minute = value.Minute
    };

    private static bool SameSummerTime(SummerTimeSettings left, SummerTimeSettings right) =>
        left.Automatic == right.Automatic && left.Enabled == right.Enabled && left.AdjustmentMinutes == right.AdjustmentMinutes
        && left.UseCustomSchedule == right.UseCustomSchedule && SameSummerRule(left.Start, right.Start) && SameSummerRule(left.End, right.End);

    private static bool SameSummerRule(SummerTimeRule left, SummerTimeRule right) =>
        left.Month == right.Month && left.UseWeekday == right.UseWeekday && left.Day == right.Day
        && left.Occurrence == right.Occurrence && left.Weekday == right.Weekday && left.Hour == right.Hour && left.Minute == right.Minute;

    private SummerTimeSettings SummerTime
    {
        get
        {
            if (!_settings.SummerTimeOverrides.TryGetValue(_timeZone.Id, out var value))
                _settings.SummerTimeOverrides[_timeZone.Id] = value = new();
            return value;
        }
    }

    private int AutomaticSummerMinutes => _selectedDate == DateOnly.FromDateTime(LocationNow())
        ? _timeZones.SavingsAt(_timeProvider.GetUtcNow(), _timeZone)
        : _timeZones.SavingsAtNoon(_selectedDate, _timeZone);

    private int EffectiveSummerMinutes => _selectedDate == DateOnly.FromDateTime(LocationNow())
        ? _timeZones.EffectiveSavingsAt(_timeProvider.GetUtcNow(), _timeZone, SummerTime)
        : _timeZones.EffectiveSavingsAtNoon(_selectedDate, _timeZone, SummerTime);

    /// <summary>Uses the bundled IANA rules; this is the default for every location.</summary>
    public bool AutomaticSummerTime
    {
        get => SummerDraft.Automatic;
        set
        {
            if (value == SummerDraft.Automatic) return;
            SelectedSummerTimeMode = _summerModes.Single(option => option.Value ==
                (value ? SummerTimeMode.Automatic : SummerTimeMode.Manual));
        }
    }

    /// <summary>Whether the seasonal adjustment applies; editable only in manual on/off mode.</summary>
    public bool SummerTimeEnabled
    {
        get => (_selectedDate == DateOnly.FromDateTime(LocationNow())
            ? _timeZones.EffectiveSavingsAt(_timeProvider.GetUtcNow(), _timeZone, SummerDraft)
            : _timeZones.EffectiveSavingsAtNoon(_selectedDate, _timeZone, SummerDraft)) != 0;
        set
        {
            if (!ForceSummerTime || value == SummerDraft.Enabled) return;
            SummerDraft.Enabled = value;
            SummerDraftChanged();
        }
    }

    /// <summary>Manual controls remain independent of the display language.</summary>
    public bool ManualSummerTime => !AutomaticSummerTime;

    /// <summary>Explicit manual seasonal offset; supports half-hour and negative adjustments.</summary>
    public int SummerTimeMinutes
    {
        get => SummerDraft.AdjustmentMinutes;
        set
        {
            value = Math.Clamp(value, -180, 180);
            if (value == SummerDraft.AdjustmentMinutes) return;
            SummerDraft.AdjustmentMinutes = value;
            SummerDraftChanged();
        }
    }

    /// <summary>All rule-derived changes for the displayed year, not hard-coded dates or country-wide assumptions.</summary>
    public string SummerTimeRules
    {
        get
        {
            if (CustomSummerTime)
            {
                if (SummerTimeError.Length > 0) return "";
                return T("SummerModeCustomDates") + " · " + _timeZone.Id + " · " + _selectedDate.Year + "\n"
                    + FormatSummerTransitions(_timeZones.CustomTransitions(_selectedDate.Year, SummerDraft));
            }
            if (_summerYear != _selectedDate.Year || !_summerZoneId.Equals(_timeZone.Id, StringComparison.OrdinalIgnoreCase))
            {
                _summerYear = _selectedDate.Year;
                _summerZoneId = _timeZone.Id;
                _summerTransitions = _timeZones.Transitions(_summerYear, _timeZone);
            }
            var heading = $"{_timeZone.Id} · {_summerYear} · {_timeZones.Version}";
            if (_summerTransitions.Count == 0) return heading + "\n" + T("SummerTimeNoChanges");
            return heading + "\n" + FormatSummerTransitions(_summerTransitions);
        }
    }

    private string FormatSummerTransitions(IReadOnlyList<SummerTimeTransition> changes) => string.Join("\n", changes.Select(change =>
                T(change.SavingsBeforeMinutes == 0 ? "SummerTimeStart" : change.SavingsAfterMinutes == 0
                    ? "SummerTimeEnd" : "SummerTimeChange") + ": \u2068" + change.Before.ToString("ddd d MMM yyyy HH:mm", Culture())
                + "\u2069" + (SelectedLanguage.IsRightToLeft ? " ← " : " → ")
                + "\u2068" + change.After.ToString("ddd d MMM yyyy HH:mm", Culture()) + "\u2069"
                // Isolate numerical offsets so RTL never reverses a sign or the before/after sequence.
                + $"\n\u2066({change.SavingsBeforeMinutes:+0;-0;0} → {change.SavingsAfterMinutes:+0;-0;0} {T("Minutes")})\u2069"));

    private void SaveSummerTime(DateOnly oldToday)
    {
        if (_selectedDate == oldToday) _selectedDate = DateOnly.FromDateTime(LocationNow());
        SaveAndRefresh();
        _logger.LogInformation("Seasonal clock preference for {Zone}: mode {Mode}, enabled {Enabled}, manual minutes {Minutes}, start {@Start}, end {@End}",
            _timeZone.Id, SelectedSummerTimeMode.Value, SummerTime.Enabled, SummerTime.AdjustmentMinutes, SummerTime.Start, SummerTime.End);
    }

    public string ManualName { get => _manualName; set => Set(ref _manualName, value); }
    public string ManualCountry { get => _manualCountry; set => Set(ref _manualCountry, value); }
    public string ManualTimeZone { get => _manualTimeZone; set => Set(ref _manualTimeZone, value); }
    public decimal? ManualLatitude { get => _manualLatitude; set => Set(ref _manualLatitude, value); }
    public decimal? ManualLongitude { get => _manualLongitude; set => Set(ref _manualLongitude, value); }
    public IReadOnlyList<string> TimeZones => _manualLocations.TimeZones;
    public IReadOnlyList<LocationInfo> SavedLocations => _settings.SavedLocations;
    public string LocationError => T(_locationErrorKey);
    public string AudioStatus => T(_audioStatusKey);
    public IReadOnlyList<string> AdhkarFiles => _settings.Adhkar.AudioFiles;
    public string? SelectedAdhkarFile { get => _selectedAdhkarFile; set => Set(ref _selectedAdhkarFile, value); }

    public LocationInfo? SelectedSavedLocation
    {
        get => _selectedSavedLocation;
        set
        {
            if (value is null || !Set(ref _selectedSavedLocation, value)) return;
            SelectLocation(new LocationResult(value, value.City, value.CountryCode));
        }
    }

    public int AudioVolume
    {
        get => _settings.AudioVolume;
        set
        {
            value = Math.Clamp(value, 0, 100);
            // Preserve immediate saves for actual edits without rewriting the profile for repeated slider values.
            if (value == _settings.AudioVolume) return;
            _settings.AudioVolume = value;
            SaveNotificationSettings();
            OnPropertyChanged();
        }
    }
    public bool AdhkarEnabled
    {
        get => _settings.Adhkar.Enabled;
        set
        {
            if (value == AdhkarEnabled) return;
            _settings.Adhkar.Enabled = value;
            SaveAdhkar();
            _logger.LogInformation("Automatic Adhkar audio {State}", value ? "enabled" : "disabled");
        }
    }
    public bool AdhkarRandomOrder
    {
        get => _settings.Adhkar.RandomOrder;
        set { if (value == AdhkarRandomOrder) return; _settings.Adhkar.RandomOrder = value; SaveAdhkar(); }
    }
    public int AdhkarIntervalMinutes
    {
        get => _settings.Adhkar.IntervalMinutes;
        set
        {
            value = Math.Clamp(value, 1, 1440);
            if (value == AdhkarIntervalMinutes) return;
            _settings.Adhkar.IntervalMinutes = value;
            SaveAdhkar();
        }
    }

    /// <summary>Validates and saves a named coordinate location, then selects it.</summary>
    public void AddManualLocation()
    {
        if (!_manualLocations.TryCreate(ManualName, (double?)ManualLatitude ?? double.NaN,
            (double?)ManualLongitude ?? double.NaN, ManualTimeZone, ManualCountry, out var location, out _locationErrorKey))
        {
            OnPropertyChanged(nameof(LocationError));
            _logger.LogWarning("Manual location validation failed: {Reason}", _locationErrorKey);
            return;
        }
        _settings.SavedLocations = [.. _settings.SavedLocations, location!];
        _selectedSavedLocation = location;
        SelectLocation(new LocationResult(location!, location!.City, location.CountryCode));
        OnPropertyChanged(nameof(SavedLocations));
        OnPropertyChanged(nameof(SelectedSavedLocation));
        OnPropertyChanged(nameof(LocationError));
        _logger.LogInformation("Saved manual location {Id}", location.Id);
    }

    /// <summary>Removes only the selected saved entry; recordings and bundled locations are never deleted.</summary>
    public void RemoveSavedLocation()
    {
        if (_selectedSavedLocation is null) return;
        var removed = _selectedSavedLocation;
        _settings.SavedLocations = _settings.SavedLocations.Where(item => !item.Id.Equals(removed.Id, StringComparison.OrdinalIgnoreCase)).ToList();
        _selectedSavedLocation = null;
        if (_location.Id.Equals(removed.Id, StringComparison.OrdinalIgnoreCase))
        {
            var fallback = _locationRepository.Get("geonames-276781");
            SelectLocation(new LocationResult(fallback, fallback.City, fallback.CountryCode));
        }
        SaveNotificationSettings();
        OnPropertyChanged(nameof(SavedLocations));
        OnPropertyChanged(nameof(SelectedSavedLocation));
        _logger.LogInformation("Removed saved location {Id}", removed.Id);
    }

    /// <summary>Assigns a local Ogg file to a prayer or sunrise; its at-time reminder must be enabled.</summary>
    public void SetPrayerAudio(PrayerNotificationRow row, string path)
    {
        if (!AudioPlayer.IsSupportedFile(path)) { SetAudioStatus("AudioFileError"); return; }
        row.AudioPath = path;
        row.AudioEnabled = true;
    }

    /// <summary>Adds unique local Ogg files in picker order without copying the recordings.</summary>
    public void AddAdhkarFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (!AudioPlayer.IsSupportedFile(path)) { SetAudioStatus("AudioFileError"); continue; }
            if (!_settings.Adhkar.AudioFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
                _settings.Adhkar.AudioFiles.Add(path);
        }
        SaveAdhkar();
    }

    /// <summary>Removes an entry from the playlist without deleting the file.</summary>
    public void RemoveAdhkarFile()
    {
        if (SelectedAdhkarFile is null) return;
        _settings.Adhkar.AudioFiles.RemoveAll(path => path.Equals(SelectedAdhkarFile, StringComparison.OrdinalIgnoreCase));
        SelectedAdhkarFile = null;
        SaveAdhkar();
    }

    /// <summary>Moves the selected recording one position earlier in sequential playback order.</summary>
    public void MoveAdhkarUp()
    {
        var files = _settings.Adhkar.AudioFiles;
        var index = files.FindIndex(path => path.Equals(SelectedAdhkarFile, StringComparison.OrdinalIgnoreCase));
        if (index <= 0) return;
        (files[index - 1], files[index]) = (files[index], files[index - 1]);
        SaveAdhkar();
    }

    /// <summary>Previews a selected local file using the same player as scheduled audio.</summary>
    public Task PreviewAudioAsync(string? path) => string.IsNullOrWhiteSpace(path)
        ? Task.CompletedTask : _audioPlayer.PlayAsync(BundledAudio.Resolve(path), AudioVolume);

    /// <summary>Stops current audio, including when the app is accessed through the tray.</summary>
    public Task StopAudioAsync() => _audioPlayer.StopAsync();

    private void SaveAdhkar()
    {
        _adhkarScheduler.Reset();
        SaveNotificationSettings();
        // New list snapshots notify ItemsControls of edits; language changes never replace selections.
        _settings.Adhkar.AudioFiles = _settings.Adhkar.AudioFiles.ToList();
        OnPropertyChanged(nameof(AdhkarFiles));
        OnPropertyChanged(nameof(AdhkarEnabled));
        OnPropertyChanged(nameof(AdhkarRandomOrder));
        OnPropertyChanged(nameof(AdhkarIntervalMinutes));
    }

    private void OnAudioStatusChanged(object? sender, string key) => Dispatcher.UIThread.Post(() =>
    {
        if (!_disposed) SetAudioStatus(key);
    });

    private void SetAudioStatus(string key)
    {
        _audioStatusKey = key;
        OnPropertyChanged(nameof(AudioStatus));
    }
}
