using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using AthanTime.Net.Models;
using AthanTime.Net.Services;
using Microsoft.Extensions.Logging;

namespace AthanTime.Net.ViewModels;

/// <summary>Represents one labeled value in a settings selector.</summary>
public sealed class SelectOption<T>(T value, string label) : INotifyPropertyChanged
{
    public T Value { get; } = value;
    public string Label { get; private set; } = label;
    public event PropertyChangedEventHandler? PropertyChanged;
    /// <summary>Changes the caption without replacing the selected option.</summary>
    public void Translate(string label)
    {
        Label = label;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
    }
}

/// <summary>Represents one localized offline place-search result.</summary>
public sealed record LocationResult(LocationInfo Location, string Title, string Subtitle);

/// <summary>Represents one displayed prayer or Shoroq/sunrise schedule row.</summary>
public sealed record PrayerRow(string Name, string Time, string Status, double Emphasis);

/// <summary>Identifies when a configured reminder occurs relative to a scheduled prayer or sunrise.</summary>
public enum PrayerAlertMoment { Before, AtTime, After }

/// <summary>Contains localized popup text for a prayer or sunrise reminder.</summary>
public sealed record PrayerAlert(
    PrayerAlertMoment Moment,
    string Title,
    string Message,
    string PrayerName,
    string Time,
    string LocationName,
    string DismissLabel);

/// <summary>Provides editable reminder settings and event-specific captions for one prayer or sunrise row.</summary>
public sealed class PrayerNotificationRow : INotifyPropertyChanged
{
    private readonly PrayerNotificationSettings _settings;
    private readonly Action _save;

    /// <summary>Creates one localized row backed by the user's JSON settings.</summary>
    public PrayerNotificationRow(
        Prayer prayer,
        string prayerName,
        string beforeLabel,
        string atTimeLabel,
        string afterLabel,
        string atTimeDescription,
        PrayerNotificationSettings settings,
        Action save)
    {
        Prayer = prayer;
        PrayerName = prayerName;
        BeforeLabel = beforeLabel;
        AtTimeLabel = atTimeLabel;
        AfterLabel = afterLabel;
        AtTimeDescription = atTimeDescription;
        _settings = settings;
        _save = save;
    }

    /// <summary>Raised when an editable reminder value changes.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    public Prayer Prayer { get; }
    public string PrayerName { get; private set; }
    public string BeforeLabel { get; private set; }
    public string AtTimeLabel { get; private set; }
    public string AfterLabel { get; private set; }
    /// <summary>Explains the displayed event time without describing sunrise as a prayer.</summary>
    public string AtTimeDescription { get; private set; }
    public bool AudioEnabled { get => _settings.AudioEnabled; set => Set(_settings.AudioEnabled, value, result => _settings.AudioEnabled = result); }
    public string AudioPath { get => _settings.AudioPath; set => Set(_settings.AudioPath, value, result => _settings.AudioPath = result); }
    public string AudioFileName => Path.GetFileName(string.IsNullOrWhiteSpace(AudioPath) ? BundledAudio.Athan : AudioPath);

    /// <summary>Updates translations without replacing a bound row or changing its settings.</summary>
    public void Translate(string name, string before, string atTime, string after, string atTimeDescription)
    {
        PrayerName = name; BeforeLabel = before; AtTimeLabel = atTime; AfterLabel = after;
        AtTimeDescription = atTimeDescription;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
    public bool NotifyBefore { get => _settings.NotifyBefore; set => Set(_settings.NotifyBefore, value, result => _settings.NotifyBefore = result); }
    public int MinutesBefore { get => _settings.MinutesBefore; set => Set(_settings.MinutesBefore, Math.Clamp(value, 1, 180), result => _settings.MinutesBefore = result); }
    public bool NotifyAtTime { get => _settings.NotifyAtTime; set => Set(_settings.NotifyAtTime, value, result => _settings.NotifyAtTime = result); }
    public bool NotifyAfter { get => _settings.NotifyAfter; set => Set(_settings.NotifyAfter, value, result => _settings.NotifyAfter = result); }
    public int MinutesAfter { get => _settings.MinutesAfter; set => Set(_settings.MinutesAfter, Math.Clamp(value, 1, 180), result => _settings.MinutesAfter = result); }

    private void Set<T>(T current, T value, Action<T> assign, [CallerMemberName] string? property = null)
    {
        if (current is string first && value is string second
            ? first.Equals(second, StringComparison.OrdinalIgnoreCase)
            : EqualityComparer<T>.Default.Equals(current, value)) return;
        assign(value);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        if (property == nameof(AudioPath)) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AudioFileName)));
        _save();
    }
}

/// <summary>Coordinates application state for the main prayer-time window.</summary>
public sealed partial class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IPrayerCalculator _prayerCalculator;
    private readonly IPrayerConventionRepository _prayerConventionRepository;
    private readonly ILocationRepository _locationRepository;
    private readonly ILocalizationService _localizationService;
    private readonly IHijriDateService _hijriDateService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<MainViewModel> _logger;
    private readonly DispatcherTimer _timer;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<DateOnly, DailyPrayerTimes> _scheduleCache = [];
    private NodaTime.DateTimeZone _timeZone = NodaTime.DateTimeZone.Utc;
    private readonly TimeZoneService _timeZones;
    private string _lastTrayText = "";
    private (DateOnly Date, Prayer Prayer, bool Tomorrow)? _lastRowKey;
    private bool _disposed;
    private AppSettings _settings;
    private LocationInfo _location;
    private DateOnly _selectedDate;
    private string _searchText = "";
    private int _searchRevision;
    private Task _searchTask = Task.CompletedTask;
    private (int Revision, string Query, string Language)? _pendingSearch;
    private IReadOnlyList<LocationResult> _searchResults = [];
    private IReadOnlyList<PrayerRow> _prayerRows = [];
    private IReadOnlyList<SelectOption<AsrJuristic>> _asrOptions = [];
    private IReadOnlyList<SelectOption<CalculationMethod>> _methodOptions = [];
    private IReadOnlyList<SelectOption<HighLatitudeRule>> _highLatitudeOptions = [];
    private IReadOnlyList<PrayerNotificationRow> _notificationRows = [];
    private DailyPrayerTimes? _daily;
    private DailyPrayerTimes? _currentDaily;
    private readonly HashSet<string> _raisedAlertKeys = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset? _lastTickUtc;
    private DateTimeOffset? _reminderNotBeforeUtc;
    private readonly DefaultAdhkarRepository _defaultAdhkar;
    private readonly LoggingLevelController _loggingLevel;
    private readonly ILoginStartupService _loginStartup;
    private IReadOnlyList<string> _defaultDhikrTexts = [];
    private int _lastDhikrHour = -1;
    private bool _loginStartupError;
    private bool _loggingError;
    private int? _lastSummerTimeMinutes;
    private bool? _lastSummerPreviewEnabled;
    private string _settingsActionStatusKey = "";

    /// <summary>Creates the view model from services supplied by dependency injection.</summary>
    public MainViewModel(
        IPrayerCalculator prayerCalculator,
        IPrayerConventionRepository prayerConventionRepository,
        ILocationRepository locationRepository,
        ILocalizationService localizationService,
        IHijriDateService hijriDateService,
        ISettingsService settingsService,
        ILogger<MainViewModel> logger,
        ManualLocationService manualLocations,
        IAudioPlayer audioPlayer,
        AdhkarScheduler adhkarScheduler,
        TimeProvider timeProvider,
        TextAdhkarScheduler textAdhkarScheduler,
        DefaultAdhkarRepository defaultAdhkar,
        LoggingLevelController loggingLevel,
        ILoginStartupService loginStartup,
        TimeZoneService timeZones)
    {
        _prayerCalculator = prayerCalculator;
        _prayerConventionRepository = prayerConventionRepository;
        _locationRepository = locationRepository;
        _localizationService = localizationService;
        _hijriDateService = hijriDateService;
        _settingsService = settingsService;
        _logger = logger;
        _manualLocations = manualLocations;
        _audioPlayer = audioPlayer;
        _adhkarScheduler = adhkarScheduler;
        _timeProvider = timeProvider;
        _textAdhkarScheduler = textAdhkarScheduler;
        _defaultAdhkar = defaultAdhkar;
        _loggingLevel = loggingLevel;
        _loginStartup = loginStartup;
        _timeZones = timeZones;
        _settings = _settingsService.Load();
        _raisedAlertKeys.UnionWith(_settings.RecentPrayerAlerts);
        _location = _settings.SavedLocations.FirstOrDefault(item => item.Id.Equals(_settings.LocationId, StringComparison.OrdinalIgnoreCase))
            ?? _locationRepository.Get(_settings.LocationId);
        _selectedSavedLocation = _settings.SavedLocations.FirstOrDefault(item => item.Id.Equals(_location.Id, StringComparison.OrdinalIgnoreCase));
        UpdateTimeZone();
        LoadLocationAdjustments();
        SummerTimeStartEditor = new(() => SummerDraft.Start, ChangeSummerRule, T);
        SummerTimeEndEditor = new(() => SummerDraft.End, ChangeSummerRule, T);
        _audioPlayer.StatusChanged += OnAudioStatusChanged;
        _settingsService.StatusChanged += OnSettingsStatusChanged;
        RebuildLocalizedCollections();
        ApplyAutomaticConvention();
        _selectedDate = DateOnly.FromDateTime(LocationNow());
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Tick();
        RefreshAll();
        _logger.LogInformation("Main view initialized for {LocationId} in {Language}", _location.Id, _settings.Language);
    }

    /// <summary>Raised when a bound property changes.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised when the tray tooltip should be refreshed.</summary>
    public event EventHandler<string>? TrayTextChanged;

    /// <summary>Raised once when a configured before/at/after prayer or sunrise reminder is due.</summary>
    public event EventHandler<PrayerAlert>? PrayerDue;

    /// <summary>Raised when a global pause begins so visible reminder windows can close.</summary>
    public event EventHandler? NotificationsPausedChanged;

    public IReadOnlyList<LanguageOption> Languages => LocalizationService.Languages;
    /// <summary>Localized method captions with stable calculation identities.</summary>
    public IReadOnlyList<SelectOption<CalculationMethod>> MethodOptions => _methodOptions;
    /// <summary>Maps the selected UI option to the unchanged calculation preset.</summary>
    public SelectOption<CalculationMethod> SelectedMethodOption
    {
        get => _methodOptions.First(option => option.Value == SelectedMethod);
        set { if (value is not null) SelectedMethod = value.Value; }
    }
    public IReadOnlyList<SelectOption<AsrJuristic>> AsrOptions => _asrOptions;
    public IReadOnlyList<SelectOption<HighLatitudeRule>> HighLatitudeOptions => _highLatitudeOptions;
    public IReadOnlyList<PrayerNotificationRow> NotificationRows => _notificationRows;
    /// <summary>Looks up new UI captions with the normal English fallback.</summary>
    public string this[string key] => T(key);

    /// <summary>Starts timers only after the desktop is ready (construction is testable without a UI loop).</summary>
    public void Start() => _timer.Start();

    public LanguageOption SelectedLanguage
    {
        get => Languages.FirstOrDefault(x => x.Code.Equals(_settings.Language, StringComparison.OrdinalIgnoreCase)) ?? Languages[0];
        set
        {
            if (value is null || value.Code.Equals(_settings.Language, StringComparison.OrdinalIgnoreCase)) return;
            _settings.Language = value.Code;
            _lastDhikrHour = -1;
            RebuildLocalizedCollections();
            SaveAndRefresh(localizationChanged: true);
        }
    }

    public CalculationMethod SelectedMethod
    {
        get => CalculationMethod.Find(_settings.MethodId);
        set
        {
            if (value is null || value.Id.Equals(_settings.MethodId, StringComparison.OrdinalIgnoreCase)) return;
            _settings.UseAutomaticConvention = false;
            _settings.MethodId = value.Id;
            SaveAndRefresh();
        }
    }

    public SelectOption<AsrJuristic> SelectedAsr
    {
        get => AsrOptions.First(x => x.Value == _settings.AsrJuristic);
        set
        {
            if (value is null || value.Value == _settings.AsrJuristic) return;
            _settings.UseAutomaticConvention = false;
            _settings.AsrJuristic = value.Value;
            SaveAndRefresh();
        }
    }

    public SelectOption<HighLatitudeRule> SelectedHighLatitude
    {
        get => HighLatitudeOptions.FirstOrDefault(x => x.Value == _settings.HighLatitudeRule)
            ?? HighLatitudeOptions[0];
        set
        {
            if (value is null || value.Value == _settings.HighLatitudeRule) return;
            _settings.UseAutomaticConvention = false;
            _settings.HighLatitudeRule = value.Value;
            SaveAndRefresh();
        }
    }

    public bool Use24HourClock
    {
        get => _settings.Use24HourClock;
        set { if (value != _settings.Use24HourClock) { _settings.Use24HourClock = value; SaveAndRefresh(calculationChanged: false); } }
    }

    public bool CloseToTray
    {
        get => _settings.CloseToTray;
        set { if (value != _settings.CloseToTray) { _settings.CloseToTray = value; SaveAndRefresh(calculationChanged: false); } }
    }

    public bool StartMinimized
    {
        get => _settings.StartMinimized;
        set { if (value != _settings.StartMinimized) { _settings.StartMinimized = value; SaveAndRefresh(calculationChanged: false); } }
    }

    /// <summary>Registers or removes a current-user, minimized login launch.</summary>
    public bool StartOnLogin
    {
        get => _loginStartup.IsEnabled();
        set
        {
            if (value == StartOnLogin) return;
            try
            {
                _loginStartup.SetEnabled(value);
                _settings.StartOnLogin = value;
                _loginStartupError = false;
                _settingsService.Save(_settings);
                OnPropertyChanged();
                OnPropertyChanged(nameof(LoginStartupError));
                _logger.LogInformation("Start on login {State}", value ? "enabled" : "disabled");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or InvalidOperationException or PlatformNotSupportedException or System.Security.SecurityException)
            {
                _logger.LogError(exception, "Could not change start-on-login registration");
                _loginStartupError = true;
                OnPropertyChanged(nameof(StartOnLogin));
                OnPropertyChanged(nameof(LoginStartupError));
            }
        }
    }

    /// <summary>Shows a localized failure only after a startup-registration error.</summary>
    public string LoginStartupError => _loginStartupError ? T("LoginStartupError") : "";

    public bool NotificationsEnabled
    {
        get => _settings.NotificationsEnabled;
        set
        {
            if (value == _settings.NotificationsEnabled) return;
            _settings.NotificationsEnabled = value;
            SaveAndRefresh(calculationChanged: false);
            _logger.LogInformation("Prayer reminders {State}", value ? "enabled" : "disabled");
        }
    }

    /// <summary>Whether all automatic prayer, audio, and text reminders are paused.</summary>
    public bool NotificationsPaused => _settings.NotificationsPausedUntilUtc > _timeProvider.GetUtcNow();

    /// <summary>Duration of a manual pause in minutes.</summary>
    public int NotificationPauseMinutes
    {
        get => _settings.NotificationPauseMinutes;
        set
        {
            value = Math.Clamp(value, 1, 1440);
            if (value == _settings.NotificationPauseMinutes) return;
            _settings.NotificationPauseMinutes = value;
            SaveAndRefresh(calculationChanged: false);
        }
    }

    public bool PrayerAudioEnabled
    {
        get => _settings.PrayerAudioEnabled;
        set
        {
            if (value == _settings.PrayerAudioEnabled) return;
            _settings.PrayerAudioEnabled = value;
            SaveAndRefresh(calculationChanged: false);
            _logger.LogInformation("Automatic prayer audio {State}", value ? "enabled" : "disabled");
        }
    }

    public bool DebugLoggingEnabled
    {
        get => _settings.DebugLoggingEnabled;
        set
        {
            if (value == _settings.DebugLoggingEnabled) return;
            _settings.DebugLoggingEnabled = value;
            _loggingLevel.SetDebug(value);
            SaveAndRefresh(calculationChanged: false);
        }
    }

    /// <summary>Enables file logging or deletes the app's Logs folder when disabled.</summary>
    public bool LoggingEnabled
    {
        get => _settings.LoggingEnabled;
        set
        {
            if (value == _settings.LoggingEnabled) return;
            try
            {
                _loggingLevel.SetEnabled(value);
                _settings.LoggingEnabled = value;
                _loggingError = false;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Disabling closes the file before deletion, so stay disabled even if deletion fails.
                _settings.LoggingEnabled = false;
                _loggingError = true;
                _logger.LogError(exception, "Could not create or remove the app's Logs folder");
            }
            SaveAndRefresh(calculationChanged: false);
            OnPropertyChanged(nameof(LoggingError));
        }
    }

    /// <summary>Shows when OS permissions or another process prevented log cleanup.</summary>
    public string LoggingError => _loggingError ? T("LoggingError") : "";

    public string PauseButtonLabel => T(NotificationsPaused ? "ResumeNotifications" : "PauseNotifications");
    public string DefaultAdhkarPreview => string.Join("\n\n", _defaultAdhkar.GetTexts(_settings.Language, LocationNow().Hour));

    /// <summary>Pauses or resumes automatic reminders, stopping current sound and text.</summary>
    public void ToggleNotificationsPause()
    {
        if (NotificationsPaused) _reminderNotBeforeUtc = _timeProvider.GetUtcNow();
        _settings.NotificationsPausedUntilUtc = NotificationsPaused
            ? null : _timeProvider.GetUtcNow().AddMinutes(NotificationPauseMinutes);
        _adhkarScheduler.Reset();
        _textAdhkarScheduler.Reset();
        TextAdhkarChanged?.Invoke(this, null);
        if (NotificationsPaused)
        {
            _ = _audioPlayer.StopAsync();
            NotificationsPausedChanged?.Invoke(this, EventArgs.Empty);
        }
        _settingsService.Save(_settings);
        OnPropertyChanged(nameof(NotificationsPaused));
        OnPropertyChanged(nameof(PauseButtonLabel));
        _logger.LogInformation("Automatic reminders {State} until {Until}", NotificationsPaused ? "paused" : "resumed", _settings.NotificationsPausedUntilUtc);
    }

    public bool UseAutomaticConvention
    {
        get => _settings.UseAutomaticConvention;
        set
        {
            if (value == _settings.UseAutomaticConvention) return;
            _settings.UseAutomaticConvention = value;
            ApplyAutomaticConvention();
            SaveAndRefresh();
        }
    }

    public bool UseDarkMode
    {
        get => _settings.UseDarkMode;
        set { if (value != _settings.UseDarkMode) { _settings.UseDarkMode = value; SaveAndRefresh(calculationChanged: false); } }
    }

    public ThemeVariant SelectedTheme => UseDarkMode ? ThemeVariant.Dark : ThemeVariant.Light;

    public int FajrAdjustment { get => Adjustment(Prayer.Fajr); set => SetAdjustment(Prayer.Fajr, value); }
    public int SunriseAdjustment { get => Adjustment(Prayer.Sunrise); set => SetAdjustment(Prayer.Sunrise, value); }
    public int DhuhrAdjustment { get => Adjustment(Prayer.Dhuhr); set => SetAdjustment(Prayer.Dhuhr, value); }
    public int AsrAdjustment { get => Adjustment(Prayer.Asr); set => SetAdjustment(Prayer.Asr, value); }
    public int MaghribAdjustment { get => Adjustment(Prayer.Maghrib); set => SetAdjustment(Prayer.Maghrib, value); }
    public int IshaAdjustment { get => Adjustment(Prayer.Isha); set => SetAdjustment(Prayer.Isha, value); }
    public int HijriAdjustment
    {
        get => _settings.HijriAdjustment;
        set
        {
            value = Math.Clamp(value, -2, 2);
            if (value == _settings.HijriAdjustment) return;
            _settings.HijriAdjustment = value;
            SaveAndRefresh(calculationChanged: false);
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_disposed) return;
            value ??= "";
            if (!Set(ref _searchText, value)) return;
            ++_searchRevision;
            _pendingSearch = null;
            if (value.Trim().Length < 2)
            {
                _searchTimer?.Stop();
                SearchResults = [];
                OnPropertyChanged(nameof(ShowNoResults));
                return;
            }
            _searchTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _searchTimer.Tick -= OnSearchTimer;
            _searchTimer.Tick += OnSearchTimer;
            _searchTimer.Stop();
            _searchTimer.Start();
        }
    }

    public IReadOnlyList<LocationResult> SearchResults
    {
        get => _searchResults;
        private set => Set(ref _searchResults, value);
    }

    public bool ShowNoResults => SearchText.Trim().Length >= 2 && SearchResults.Count == 0;
    public IReadOnlyList<PrayerRow> PrayerRows { get => _prayerRows; private set => Set(ref _prayerRows, value); }
    public FlowDirection FlowDirection => SelectedLanguage.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public string AppName => App.IsBeta ? $"{T("AppName")} ({T("Beta")})" : T("AppName");
    public string Tagline => T("Tagline");
    public string NextPrayerLabel => T("NextPrayer");
    public string TodayLabel => T("Today");
    public string LocationLabel => T("Location");
    public string SearchPlaceLabel => T("SearchPlace");
    public string SettingsLabel => T("Settings");
    public string MethodLabel => T("Method");
    public string AsrLabel => T("Asr");
    public string HighLatitudeLabel => T("HighLatitude");
    public string ClockLabel => T("Clock");
    public string DarkModeLabel => T("DarkMode");
    public string QiblaLabel => T("Qibla");
    public string MidnightLabel => T("Midnight");
    public string PreviousLabel => T("Previous");
    public string NextLabel => T("Next");
    public string CloseToTrayLabel => T("CloseToTray");
    public string NotificationsLabel => T("Notifications");
    public string AutomaticConventionLabel => T("AutomaticConvention");
    public string ConventionLabel => T("Convention");
    public string HelpLabel => T("Help");
    public string AboutLabel => T("About");
    public string OkLabel => T("OK");
    public string TrayShowLabel => T("TrayShow");
    public string TrayExitLabel => T("TrayExit");
    public string AdjustmentsLabel => T("Adjustments");
    public string HijriAdjustmentLabel => T("HijriAdjustment");
    public string NoResultsLabel => T("NoResults");
    public string OfflineLabel => $"{T("Offline")} · {_locationRepository.Count:N0}";
    public string LocationName => _location.Name(_settings.Language);
    public string CountryName => _location.CountryName(_settings.Language);
    public string Coordinates => $"{_location.Latitude:0.####}, {_location.Longitude:0.####}";
    public string ConventionDescription
    {
        get
        {
            var convention = _prayerConventionRepository.Resolve(_location);
            return _settings.UseAutomaticConvention
                ? $"{T("Method" + convention.MethodId)} · {T("ConventionHelp")}"
                : $"{T("Method" + SelectedMethod.Id)} · {T("ManualOverride")}";
        }
    }
    public string HelpText => T("HelpText") + "\n\n" + T("SummerDraftHelp") + "\n\n" + T("LocationAdjustmentsHelp")
        + "\n\n" + T("SettingsTransferHelp") + "\n\n" + T("CoordinateHelp") + "\n\n" + T("AudioHelp")
        + "\n\n" + T("PlaylistHelp") + "\n\n" + T("TextAdhkarHelp") + "\n\n" + T("TrayUnavailable");
    public string AboutText => string.Format(CultureInfo.InvariantCulture, T("AboutText"), AppVersion);
    public string AppVersion => App.ReleaseVersion;
    public string CurrentClock { get; private set; } = "";
    public string DisplayDate { get; private set; } = "";
    public string HijriDate { get; private set; } = "";
    public string QiblaValue { get; private set; } = "";
    public string MidnightValue { get; private set; } = "";
    public string NextPrayerName { get; private set; } = "";
    public string NextPrayerTime { get; private set; } = "";
    public string Countdown { get; private set; } = "";
    public double DayProgress { get; private set; }

    /// <summary>Localized persistence, recovery and transfer outcome; never silently hides failures.</summary>
    public string SettingsStatus => T(_settingsService.StatusKey.Length > 0 ? _settingsService.StatusKey : _settingsActionStatusKey);
    /// <summary>Hides the shared status banner when storage has no result or warning to report.</summary>
    public bool HasSettingsStatus => SettingsStatus.Length > 0;

    /// <summary>Exports the JSON profile without bundling logs, coordinates databases or external audio files.</summary>
    public bool ExportSettings(string path) => _settingsService.Export(_settings, path);

    /// <summary>Reads and validates a proposed import without modifying active settings.</summary>
    public AppSettings? ReadSettingsImport(string path)
    {
        var imported = _settingsService.ReadImport(path);
        if (imported is null) return null;
        var place = imported.SavedLocations.FirstOrDefault(item => item.Id.Equals(imported.LocationId, StringComparison.OrdinalIgnoreCase))
            ?? _locationRepository.Get(imported.LocationId);
        if (place.Id.Equals(imported.LocationId, StringComparison.OrdinalIgnoreCase)) return imported;
        _settingsActionStatusKey = "SettingsImportFailed";
        OnPropertyChanged(nameof(SettingsStatus));
        OnPropertyChanged(nameof(HasSettingsStatus));
        _logger.LogWarning("Settings import has an unknown location {LocationId}; active preferences unchanged", imported.LocationId);
        return null;
    }

    /// <summary>Applies a user-confirmed import while retaining this device's startup, logging and reminder history.</summary>
    public void ApplyImportedSettings(AppSettings imported)
    {
        imported.StartOnLogin = _settings.StartOnLogin;
        imported.LoggingEnabled = _settings.LoggingEnabled;
        imported.DebugLoggingEnabled = _settings.DebugLoggingEnabled;
        imported.RecentPrayerAlerts = _settings.RecentPrayerAlerts;
        imported.NotificationsPausedUntilUtc = null;
        _settings = imported;
        _selectedAdhkarFile = null;
        _selectedAdhkarText = null;
        _adhkarTextDraft = "";
        _locationErrorKey = "";
        _location = _settings.SavedLocations.FirstOrDefault(item => item.Id.Equals(_settings.LocationId, StringComparison.OrdinalIgnoreCase))
            ?? _locationRepository.Get(_settings.LocationId);
        _selectedSavedLocation = _settings.SavedLocations.FirstOrDefault(item => item.Id.Equals(_location.Id, StringComparison.OrdinalIgnoreCase));
        _summerDraftZone = "";
        LoadLocationAdjustments();
        UpdateTimeZone();
        ApplyAutomaticConvention();
        _selectedDate = DateOnly.FromDateTime(LocationNow());
        _asrOptions = [];
        RebuildLocalizedCollections();
        _lastDhikrHour = -1;
        _adhkarScheduler.Reset();
        _textAdhkarScheduler.Reset();
        TextAdhkarChanged?.Invoke(this, null);
        NotificationsPausedChanged?.Invoke(this, EventArgs.Empty);
        _ = _audioPlayer.StopAsync();
        SearchText = "";
        // Import changes calculation inputs too; language-only refresh deliberately does not clear this cache.
        _scheduleCache.Clear();
        SaveAndRefresh(localizationChanged: true);
        if (_settingsService.StatusKey.Length == 0) _settingsActionStatusKey = "SettingsImported";
        OnPropertyChanged(nameof(SettingsStatus));
        OnPropertyChanged(nameof(HasSettingsStatus));
        _logger.LogInformation("User imported settings for {LocationId}; device startup/logging preferences retained", _location.Id);
    }

    private void OnSettingsStatusChanged(object? sender, EventArgs args)
    {
        _settingsActionStatusKey = "";
        OnPropertyChanged(nameof(SettingsStatus));
        OnPropertyChanged(nameof(HasSettingsStatus));
        OnPropertyChanged(nameof(SummerTimeEditStatus));
        OnPropertyChanged(nameof(CanApplySummerTime));
    }

    /// <summary>Selects an offline place and applies its convention when automatic selection is enabled.</summary>
    public void SelectLocation(LocationResult result)
    {
        StoreLocationAdjustments();
        _location = result.Location;
        _settings.LocationId = _location.Id;
        LoadLocationAdjustments();
        _summerDraftZone = "";
        _selectedSavedLocation = _settings.SavedLocations.FirstOrDefault(item => item.Id.Equals(_location.Id, StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(SelectedSavedLocation));
        UpdateTimeZone();
        ApplyAutomaticConvention();
        _selectedDate = DateOnly.FromDateTime(LocationNow());
        SearchText = "";
        SaveAndRefresh();
        _logger.LogInformation("Selected location {LocationId} with method {MethodId}", _location.Id, _settings.MethodId);
    }

    /// <summary>Moves the displayed schedule to the previous local date.</summary>
    public void PreviousDay() { _selectedDate = _selectedDate.AddDays(-1); RefreshAll(); }

    /// <summary>Moves the displayed schedule to the next local date.</summary>
    public void NextDay() { _selectedDate = _selectedDate.AddDays(1); RefreshAll(); }

    /// <summary>Returns the displayed schedule to today at the selected place.</summary>
    public void Today() { _selectedDate = DateOnly.FromDateTime(LocationNow()); RefreshAll(); }

    /// <summary>Stops the one-second UI timer.</summary>
    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        _searchTimer?.Stop();
        _pendingSearch = null;
        _audioPlayer.StatusChanged -= OnAudioStatusChanged;
        _settingsService.StatusChanged -= OnSettingsStatusChanged;
    }

    private void RefreshAll()
    {
        RefreshSummerTimeEditors();
        _lastRowKey = null;
        _daily = Calculate(_selectedDate);
        var today = DateOnly.FromDateTime(LocationNow());
        _currentDaily = _selectedDate == today ? _daily : Calculate(today);
        var culture = Culture();
        var timeFormat = _settings.Use24HourClock ? "HH:mm" : "h:mm tt";
        DisplayDate = _selectedDate.ToDateTime(TimeOnly.MinValue).ToString("dddd, d MMMM yyyy", culture);
        HijriDate = _hijriDateService.Format(_selectedDate, _settings.Language, _settings.HijriAdjustment);
        QiblaValue = $"{_daily.QiblaBearing:0.0}° {T("Direction")}";
        MidnightValue = _daily.Midnight.ToString(timeFormat, culture);
        RefreshDynamicValues();
        RaiseAll();
    }

    /// <summary>Updates the clock and due reminders using the injected clock.</summary>
    public void Tick()
    {
        var nowUtc = _timeProvider.GetUtcNow();
        if (_lastTickUtc is { } previousUtc && (nowUtc < previousUtc || nowUtc - previousUtc > TimeSpan.FromSeconds(5)))
        {
            // A resumed timer starts fresh intervals instead of releasing missed audio or readings.
            _reminderNotBeforeUtc = nowUtc;
            _adhkarScheduler.Reset();
            _textAdhkarScheduler.Reset();
            TextAdhkarChanged?.Invoke(this, null);
            _logger.LogDebug("Reminder clocks restarted after UTC clock gap {Gap}", nowUtc - previousUtc);
        }
        _lastTickUtc = nowUtc;
        var summerMinutes = EffectiveSummerMinutes;
        if (_lastSummerTimeMinutes != summerMinutes)
        {
            var activeChanged = _lastSummerTimeMinutes is null
                || (_lastSummerTimeMinutes.Value != 0) != (summerMinutes != 0);
            _lastSummerTimeMinutes = summerMinutes;
            if (activeChanged) OnPropertyChanged(nameof(ActiveSummerTimeEnabled));
            OnPropertyChanged(nameof(ActiveSummerTimeDescription));
            _logger.LogDebug("Seasonal status for {Zone} on {Date}: active {Active}, automatic {Automatic}",
                _timeZone.Id, _selectedDate, summerMinutes != 0, SummerTime.Automatic);
        }
        // A draft can cross a different seasonal boundary from the committed clock.
        var previewEnabled = SummerTimeEnabled;
        if (_lastSummerPreviewEnabled != previewEnabled)
        {
            _lastSummerPreviewEnabled = previewEnabled;
            OnPropertyChanged(nameof(SummerTimeEnabled));
        }
        if (_settings.NotificationsPausedUntilUtc is { } until && until <= nowUtc)
        {
            _settings.NotificationsPausedUntilUtc = null;
            _reminderNotBeforeUtc = nowUtc;
            _settingsService.Save(_settings);
            _adhkarScheduler.Reset();
            _textAdhkarScheduler.Reset();
            OnPropertyChanged(nameof(NotificationsPaused));
            OnPropertyChanged(nameof(PauseButtonLabel));
            _logger.LogInformation("Automatic reminder pause expired");
        }
        var locationToday = DateOnly.FromDateTime(LocationNow());
        if (_currentDaily?.Date != locationToday)
        {
            if (_selectedDate == _currentDaily?.Date) _selectedDate = locationToday;
            _scheduleCache.Clear();
            RefreshAll();
        }
        else
        {
            var previous = (CurrentClock, Countdown, DayProgress, NextPrayerName, NextPrayerTime);
            RefreshDynamicValues();
            if (previous.CurrentClock != CurrentClock) OnPropertyChanged(nameof(CurrentClock));
            if (previous.Countdown != Countdown) OnPropertyChanged(nameof(Countdown));
            if (previous.DayProgress != DayProgress) OnPropertyChanged(nameof(DayProgress));
            if (previous.NextPrayerName != NextPrayerName) OnPropertyChanged(nameof(NextPrayerName));
            if (previous.NextPrayerTime != NextPrayerTime) OnPropertyChanged(nameof(NextPrayerTime));
        }
        if (NotificationsPaused) return;
        CheckPrayerAlert(LocationNow(), nowUtc);
        var file = _adhkarScheduler.TakeDue(_settings.Adhkar, nowUtc, _audioPlayer.IsBusy);
        if (file is not null) _ = _audioPlayer.PlayAsync(BundledAudio.Resolve(file), _settings.AudioVolume, interrupt: false);
        var localHour = LocationNow().Hour;
        if (_lastDhikrHour != localHour)
        {
            _lastDhikrHour = localHour;
            _defaultDhikrTexts = _defaultAdhkar.GetTexts(_settings.Language, localHour);
            OnPropertyChanged(nameof(DefaultAdhkarPreview));
        }
        if (_textAdhkarScheduler.Tick(_settings.TextAdhkar, nowUtc, _defaultDhikrTexts))
            TextAdhkarChanged?.Invoke(this, _textAdhkarScheduler.CurrentText);
    }

    private void RefreshDynamicValues()
    {
        if (_daily is null) return;
        var now = LocationNow();
        var nowUtc = _timeProvider.GetUtcNow();
        CurrentClock = now.ToString(_settings.Use24HourClock ? "HH:mm:ss" : "h:mm:ss tt", Culture());
        var selectedPrayers = FindCurrentAndNext(_daily, nowUtc);
        RefreshSelectedSchedule(now, nowUtc, selectedPrayers);
        RefreshTrayStatus(now, nowUtc, selectedPrayers);
    }

    // The browsed date drives the main display; the tray always follows today's real UTC events.
    private void RefreshSelectedSchedule(DateTime now, DateTimeOffset nowUtc,
        (PrayerTime? Current, PrayerTime? Next) selectedPrayers)
    {
        var culture = Culture();
        var timeFormat = _settings.Use24HourClock ? "HH:mm" : "h:mm tt";
        var isToday = _selectedDate == DateOnly.FromDateTime(now);
        var next = isToday ? selectedPrayers.Next : _daily!.Times.First(item => item.Prayer != Prayer.Sunrise);
        var tomorrow = false;
        if (next is null)
        {
            var tomorrowDate = _selectedDate.AddDays(1);
            var nextDay = Calculate(tomorrowDate);
            next = nextDay.Times.First(x => x.Prayer == Prayer.Fajr);
            tomorrow = true;
        }

        NextPrayerName = PrayerName(next.Prayer) + (tomorrow ? $" · {T("Tomorrow")}" : "");
        NextPrayerTime = next.LocalTime.ToString(timeFormat, culture);
        var remaining = PrayerUtc(next) - nowUtc;
        Countdown = isToday && remaining > TimeSpan.Zero
            ? remaining.TotalHours >= 1
                ? $"{(int)remaining.TotalHours}:{remaining.Minutes:00}:{remaining.Seconds:00}"
                : $"{remaining.Minutes}:{remaining.Seconds:00}"
            : "";

        var current = selectedPrayers.Current;
        var previous = current is not null ? PrayerUtc(current)
            : _timeZones.ToUtc(_selectedDate.ToDateTime(TimeOnly.MinValue), _timeZone, SummerTime);
        var segment = PrayerUtc(next) - previous;
        DayProgress = isToday && segment.TotalSeconds > 0
            ? Math.Clamp((nowUtc - previous).TotalSeconds / segment.TotalSeconds * 100, 0, 100)
            : 0;

        var rowKey = (_daily!.Date, next.Prayer, tomorrow);
        if (_lastRowKey != rowKey)
        {
            _lastRowKey = rowKey;
            PrayerRows = _daily.Times.Select(x => new PrayerRow(
            PrayerName(x.Prayer), x.LocalTime.ToString(timeFormat, culture),
            x.Prayer == next.Prayer && !tomorrow ? T("NextPrayer") : "",
                x.Prayer == next.Prayer && !tomorrow ? 1 : 0.45)).ToArray();
        }
    }

    private void RefreshTrayStatus(DateTime now, DateTimeOffset nowUtc,
        (PrayerTime? Current, PrayerTime? Next) selectedPrayers)
    {
        var trayPrayers = ReferenceEquals(_daily, _currentDaily) ? selectedPrayers : FindCurrentAndNext(_currentDaily!, nowUtc);
        var trayCurrent = trayPrayers.Current;
        if (trayCurrent is null)
            trayCurrent = Calculate(DateOnly.FromDateTime(now).AddDays(-1)).Times.Last(item => item.Prayer == Prayer.Isha);
        var trayNext = trayPrayers.Next
            ?? Calculate(DateOnly.FromDateTime(now).AddDays(1)).Times.First(item => item.Prayer == Prayer.Fajr);
        var trayRemaining = PrayerUtc(trayNext) - nowUtc;
        var trayText = $"{T("CurrentPrayer")}: {PrayerName(trayCurrent.Prayer)} · {T("NextPrayer")}: "
            + $"{PrayerName(trayNext.Prayer)} {FormatRemaining(trayRemaining)} · {LocationName}";
        if (NotificationsPaused) trayText = T("NotificationsPaused") + " | " + trayText;
        if (!trayText.Equals(_lastTrayText, StringComparison.OrdinalIgnoreCase))
        {
            _lastTrayText = trayText;
            TrayTextChanged?.Invoke(this, trayText);
        }
    }

    // Both displays use the same six cached events. Avoid per-tick filtered lists and repeated scans on Today.
    private (PrayerTime? Current, PrayerTime? Next) FindCurrentAndNext(DailyPrayerTimes daily, DateTimeOffset nowUtc)
    {
        PrayerTime? current = null, next = null;
        foreach (var prayer in daily.Times)
        {
            if (prayer.Prayer == Prayer.Sunrise) continue;
            if (PrayerUtc(prayer) <= nowUtc) current = prayer;
            else next ??= prayer;
        }
        return (current, next);
    }

    private void CheckPrayerAlert(DateTime now, DateTimeOffset nowUtc)
    {
        if (!_settings.NotificationsEnabled || _currentDaily is null
            || _currentDaily.Date != DateOnly.FromDateTime(now)) return;
        // Adjacent days are required for before/after reminders that cross local midnight.
        foreach (var prayer in Enumerable.Range(-1, 3).SelectMany(offset => Calculate(_currentDaily.Date.AddDays(offset)).Times))
        {
            var notification = _settings.PrayerNotifications[prayer.Prayer];
            var prayerUtc = PrayerUtc(prayer);
            if (notification.NotifyBefore)
                RaiseAlertAt(prayer, PrayerAlertMoment.Before, prayerUtc.AddMinutes(-notification.MinutesBefore), nowUtc);
            if (notification.NotifyAtTime)
                RaiseAlertAt(prayer, PrayerAlertMoment.AtTime, prayerUtc, nowUtc);
            if (notification.NotifyAfter)
                RaiseAlertAt(prayer, PrayerAlertMoment.After, prayerUtc.AddMinutes(notification.MinutesAfter), nowUtc);
        }
    }

    private DateTime LocationNow() => _timeZones.ToLocal(_timeProvider.GetUtcNow(), _timeZone, SummerTime);

    // Real prayer instants avoid ambiguous or missing local hours at clock changes.
    private DateTimeOffset PrayerUtc(PrayerTime prayer) => prayer.UtcTime
        ?? _timeZones.ToUtc(prayer.LocalTime, _timeZone, SummerTime);

    private void UpdateTimeZone()
    {
        _timeZone = _timeZones.Find(_location.TimeZoneId);
    }

    private CultureInfo Culture()
    {
        var name = _settings.Language.ToLowerInvariant() switch
        {
            "ar" => "ar-LB", "fr" => "fr-FR", "es" => "es-ES", "tr" => "tr-TR",
            "de" => "de-DE", "id" => "id-ID", "ur" => "ur-PK", _ => "en-US"
        };
        return CultureInfo.GetCultureInfo(name);
    }

    private string PrayerName(Prayer prayer) => T(prayer == Prayer.Asr ? "AsrPrayer" : prayer.ToString());
    private string T(string key) => _localizationService.Get(_settings.Language, key);

    private void SaveAndRefresh(bool localizationChanged = false, bool calculationChanged = true)
    {
        if (!localizationChanged && calculationChanged) _scheduleCache.Clear();
        _settingsService.Save(_settings);
        RefreshAll();
        if (localizationChanged)
        {
            OnPropertyChanged(nameof(AsrOptions));
            OnPropertyChanged(nameof(HighLatitudeOptions));
            UpdateSearchResults();
        }
    }

    private void RaiseAll()
    {
        OnPropertyChanged(string.Empty);
        OnPropertyChanged("Item");
        OnPropertyChanged("Item[]");
    }

    private void ApplyAutomaticConvention()
    {
        if (!_settings.UseAutomaticConvention) return;
        var convention = _prayerConventionRepository.Resolve(_location);
        _settings.MethodId = convention.MethodId;
        _settings.AsrJuristic = convention.AsrJuristic;
        _settings.HighLatitudeRule = convention.HighLatitudeRule;
    }

    private void RebuildLocalizedCollections()
    {
        if (_asrOptions.Count > 0)
        {
            foreach (var option in _methodOptions) option.Translate(T("Method" + option.Value.Id));
            foreach (var option in _asrOptions) option.Translate(T(option.Value.ToString()));
            foreach (var option in _highLatitudeOptions) option.Translate(T(option.Value.ToString()));
            foreach (var row in _notificationRows) row.Translate(PrayerName(row.Prayer),
                T(row.Prayer == Prayer.Sunrise ? "NotifyBeforeSunrise" : "NotifyBefore"),
                T(row.Prayer == Prayer.Sunrise ? "NotifyAtSunrise" : "NotifyAtTime"),
                T(row.Prayer == Prayer.Sunrise ? "NotifyAfterSunrise" : "NotifyAfter"),
                T(row.Prayer == Prayer.Sunrise ? "AtSunriseTiming" : "AtPrayerTiming"));
            return;
        }
        _methodOptions = CalculationMethod.All.Select(method => new SelectOption<CalculationMethod>(method, T("Method" + method.Id))).ToArray();
        _asrOptions =
        [
            new(AsrJuristic.Standard, T("Standard")),
            new(AsrJuristic.Hanafi, T("Hanafi"))
        ];
        _highLatitudeOptions =
        [
            new(HighLatitudeRule.AngleBased, T("AngleBased")),
            new(HighLatitudeRule.MiddleOfNight, T("MiddleOfNight")),
            new(HighLatitudeRule.OneSeventh, T("OneSeventh"))
        ];
        _notificationRows = Enum.GetValues<Prayer>()
            .Select(prayer => new PrayerNotificationRow(
                prayer,
                PrayerName(prayer),
                T(prayer == Prayer.Sunrise ? "NotifyBeforeSunrise" : "NotifyBefore"),
                T(prayer == Prayer.Sunrise ? "NotifyAtSunrise" : "NotifyAtTime"),
                T(prayer == Prayer.Sunrise ? "NotifyAfterSunrise" : "NotifyAfter"),
                T(prayer == Prayer.Sunrise ? "AtSunriseTiming" : "AtPrayerTiming"),
                _settings.PrayerNotifications[prayer],
                SaveNotificationSettings))
            .ToArray();
    }

    private void SaveNotificationSettings()
    {
        _settingsService.Save(_settings);
        _logger.LogDebug("Saved per-prayer notification settings");
    }

    private DailyPrayerTimes Calculate(DateOnly date)
    {
        if (_scheduleCache.TryGetValue(date, out var cached)) return cached;
        if (_scheduleCache.Count >= 10) _scheduleCache.Clear();
        var result = _prayerCalculator.Calculate(date, _location, SelectedMethod, _settings.AsrJuristic,
            _settings.HighLatitudeRule, _settings.MinuteAdjustments, SummerTime);
        _scheduleCache[date] = result;
        return result;
    }

    private static string FormatRemaining(TimeSpan remaining) => remaining.TotalHours >= 1
        ? $"{(int)remaining.TotalHours}:{remaining.Minutes:00}"
        : $"0:{Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes)):00}";

    private void RaiseAlertAt(PrayerTime prayer, PrayerAlertMoment moment, DateTimeOffset alertTime, DateTimeOffset now)
    {
        alertTime = alertTime.ToUniversalTime();
        now = now.ToUniversalTime();
        if (alertTime > now || (_reminderNotBeforeUtc is { } resumedUtc && alertTime < resumedUtc)
            || alertTime.Year != now.Year || alertTime.Month != now.Month || alertTime.Day != now.Day
            || alertTime.Hour != now.Hour || alertTime.Minute != now.Minute) return;

        var key = $"{_location.Id}:{prayer.Prayer}:{moment}:{alertTime.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture)}";
        if (!_raisedAlertKeys.Add(key)) return;
        // Persist before dispatch so restarting in this UTC minute cannot repeat the same reminder.
        _settings.RecentPrayerAlerts.Add(key);
        while (_settings.RecentPrayerAlerts.Count > 128)
        {
            _raisedAlertKeys.Remove(_settings.RecentPrayerAlerts[0]);
            _settings.RecentPrayerAlerts.RemoveAt(0);
        }
        _settingsService.Save(_settings);

        var prayerName = PrayerName(prayer.Prayer);
        var notification = _settings.PrayerNotifications[prayer.Prayer];
        if (moment == PrayerAlertMoment.AtTime && _settings.PrayerAudioEnabled && notification.AudioEnabled)
            _ = _audioPlayer.PlayAsync(BundledAudio.Resolve(string.IsNullOrWhiteSpace(notification.AudioPath)
                ? "bundled:adhan" : notification.AudioPath), _settings.AudioVolume);
        var minutes = moment == PrayerAlertMoment.Before ? notification.MinutesBefore : notification.MinutesAfter;
        // Share delivery/history identities while keeping sunrise separate in user-facing wording.
        var sunrise = prayer.Prayer == Prayer.Sunrise;
        var messageKey = moment switch
        {
            PrayerAlertMoment.Before => sunrise ? "BeforeSunriseMessage" : "BeforePrayerMessage",
            PrayerAlertMoment.After => sunrise ? "AfterSunriseMessage" : "AfterPrayerMessage",
            _ => sunrise ? "AtSunriseMessage" : "AtPrayerMessage"
        };
        var message = moment == PrayerAlertMoment.AtTime
            ? string.Format(Culture(), T(messageKey), prayerName)
            : string.Format(Culture(), T(messageKey), prayerName, minutes);
        PrayerDue?.Invoke(this, new PrayerAlert(moment, T(sunrise ? "SunriseReminder" : "PrayerReminder"), message, prayerName,
            prayer.LocalTime.ToString(_settings.Use24HourClock ? "HH:mm" : "h:mm tt", Culture()),
            LocationName, T("OK")));
        _logger.LogInformation("Raised {Moment} reminder for {Prayer} at {LocationId}", moment, prayer.Prayer, _location.Id);
    }

    private void UpdateSearchResults()
    {
        if (_disposed) return;
        _searchTimer?.Stop();
        var revision = ++_searchRevision;
        var query = _searchText;
        var language = _settings.Language;
        if (query.Trim().Length < 2)
        {
            _pendingSearch = null;
            SearchResults = [];
            OnPropertyChanged(nameof(ShowNoResults));
            return;
        }
        _pendingSearch = (revision, query, language);
        if (_searchTask.IsCompleted) _searchTask = SearchLocationsAsync();
    }

    // UI-owned state and captured dispatcher context keep one query active and only the latest request pending.
    // A running SQLite call finishes/disposes normally; clearing/disposal discards its output and pending work.
    private async Task SearchLocationsAsync()
    {
        // Publish the owned task before a fast query can finish or a result binding can request another search.
        await Task.Yield();
        while (!_disposed && _pendingSearch is { } request)
        {
            _pendingSearch = null;
            try
            {
                var results = await Task.Run(() => _locationRepository.Search(request.Query, request.Language)
                    .Select(x => new LocationResult(x, x.Name(request.Language),
                        $"{x.CountryName(request.Language)}  ·  {x.Latitude:0.####}, {x.Longitude:0.####}"))
                    .ToArray());
                if (_disposed || request.Revision != _searchRevision) continue;
                SearchResults = results;
                OnPropertyChanged(nameof(ShowNoResults));
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Offline location search failed for {Query}", request.Query);
            }
        }
    }

    private void OnSearchTimer(object? sender, EventArgs args)
    {
        _searchTimer?.Stop();
        UpdateSearchResults();
    }

    private int Adjustment(Prayer prayer) => _settings.MinuteAdjustments.GetValueOrDefault(prayer);

    private void SetAdjustment(Prayer prayer, int value)
    {
        value = Math.Clamp(value, -60, 60);
        if (Adjustment(prayer) == value) return;
        if (value == 0) _settings.MinuteAdjustments.Remove(prayer);
        else _settings.MinuteAdjustments[prayer] = value;
        StoreLocationAdjustments();
        SaveAndRefresh();
    }

    private void LoadLocationAdjustments()
    {
        // Old profiles have one correction map. It belongs only to their selected location.
        if (_settings.LocationMinuteAdjustments.Count == 0)
            StoreLocationAdjustments();
        _settings.MinuteAdjustments = _settings.LocationMinuteAdjustments.TryGetValue(_location.Id, out var values)
            ? new(values) : [];
    }

    private void StoreLocationAdjustments() =>
        _settings.LocationMinuteAdjustments[_settings.LocationId] = new(_settings.MinuteAdjustments);

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (field is string first && value is string second
            ? first.Equals(second, StringComparison.OrdinalIgnoreCase)
            : EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(property);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
