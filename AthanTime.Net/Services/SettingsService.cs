using System.Text.Json;
using System.Text.Json.Serialization;
using System.Reflection;
using AthanTime.Net.Models;
using Microsoft.Extensions.Logging;

namespace AthanTime.Net.Services;

/// <summary>Loads and saves user settings as JSON in the platform application-data folder.</summary>
public interface ISettingsService
{
    /// <summary>Culture-neutral key for an unresolved profile warning, otherwise the latest transfer result.</summary>
    string StatusKey { get; }

    /// <summary>Notifies the UI when the storage status changes.</summary>
    event EventHandler? StatusChanged;

    /// <summary>Loads settings or returns safe defaults when the file is unavailable.</summary>
    AppSettings Load();

    /// <summary>Persists settings as formatted JSON.</summary>
    void Save(AppSettings settings);

    /// <summary>Exports a normalized JSON snapshot without changing the active profile.</summary>
    bool Export(AppSettings settings, string path);

    /// <summary>Reads and validates an import without applying it or changing the active profile.</summary>
    AppSettings? ReadImport(string path);
}

/// <inheritdoc />
public sealed class SettingsService(ILogger<SettingsService> logger, ManualLocationService manualLocations,
    string settingsPath) : ISettingsService
{
    private const string DefaultResourceName = "AthanTime.Net.Data.default-settings.json";
    private const int CurrentSchemaVersion = 5;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly string _path = Path.GetFullPath(settingsPath);
    private readonly ILogger<SettingsService> _logger = logger;
    private readonly ManualLocationService _manualLocations = manualLocations;
    private string _profileStatusKey = "";
    private string _transferStatusKey = "";

    /// <inheritdoc />
    public string StatusKey => _profileStatusKey.Length > 0 ? _profileStatusKey : _transferStatusKey;

    /// <inheritdoc />
    public event EventHandler? StatusChanged;

    /// <inheritdoc />
    public AppSettings Load()
    {
        SetStatus("", profile: true, clearTransfer: true);
        if (TryRead(_path, out var settings, out var futureSchema, out var primaryFailed))
        {
            _logger.LogDebug("Loaded settings from {Path}", _path);
            return settings!;
        }

        // A newer app's profile must not be replaced by an older backup or defaults.
        var backupFailed = false;
        if (!futureSchema && TryRead(_path + ".bak", out settings, out _, out backupFailed))
        {
            _logger.LogWarning("Recovered settings from the last-known-good backup at {Path}", _path + ".bak");
            SetStatus("SettingsRecovered", profile: true);
            return settings!;
        }

        if (primaryFailed || backupFailed)
            SetStatus("SettingsLoadFailed", profile: true);
        else
            _logger.LogInformation("No settings file exists at {Path}; defaults are used", _path);
        return Normalize(LoadDefaults());
    }

    /// <inheritdoc />
    public void Save(AppSettings settings)
    {
        try
        {
            var validPrimary = TryRead(_path, out var previousSettings, out var futureSchema, out _);
            if (futureSchema || settings.SchemaVersion > CurrentSchemaVersion)
                throw new JsonException("A newer settings schema cannot be overwritten by this application version.");

            // Never promote a damaged primary into the last-known-good backup.
            if (validPrimary) WriteAtomic(_path + ".bak", JsonSerializer.Serialize(previousSettings, _jsonOptions));
            settings.LocationMinuteAdjustments ??= new(StringComparer.OrdinalIgnoreCase);
            settings.LocationMinuteAdjustments[settings.LocationId] = NormalizeAdjustments(settings.MinuteAdjustments);
            var json = JsonSerializer.Serialize(settings, _jsonOptions);
            WriteAtomic(_path, json);
            if (!validPrimary && !TryRead(_path + ".bak", out _, out _, out _)) WriteAtomic(_path + ".bak", json);
            if (string.Equals(_profileStatusKey, "SettingsSaveFailed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(_profileStatusKey, "SettingsLoadFailed", StringComparison.OrdinalIgnoreCase))
                SetStatus("", profile: true);
            _logger.LogDebug("Saved settings to {Path}", _path);
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            _logger.LogError(exception, "Settings could not be saved to {Path}", _path);
            SetStatus("SettingsSaveFailed", profile: true);
        }
    }

    /// <inheritdoc />
    public bool Export(AppSettings settings, string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (IsProfilePath(fullPath)) throw new IOException("Export must not overwrite the active profile or its backup.");
            if (settings.SchemaVersion > CurrentSchemaVersion)
                throw new JsonException("A newer settings schema cannot be exported by this application version.");
            var snapshot = Normalize(JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, _jsonOptions), _jsonOptions)!);
            snapshot.LocationMinuteAdjustments[snapshot.LocationId] = NormalizeAdjustments(settings.MinuteAdjustments);
            snapshot.MinuteAdjustments = NormalizeAdjustments(settings.MinuteAdjustments);
            WriteAtomic(fullPath, JsonSerializer.Serialize(snapshot, _jsonOptions));
            SetStatus("SettingsExported");
            _logger.LogInformation("Exported settings to {Path}", fullPath);
            return true;
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            _logger.LogWarning(exception, "Settings export failed at {Path}", path);
            SetStatus("SettingsExportFailed");
            return false;
        }
    }

    /// <inheritdoc />
    public AppSettings? ReadImport(string path)
    {
        if (TryRead(path, out var settings, out _, out _, forImport: true))
        {
            SetStatus("");
            _logger.LogInformation("Validated settings import from {Path}; it has not been applied", path);
            return settings;
        }
        _logger.LogWarning("Settings import was rejected at {Path}", path);
        SetStatus("SettingsImportFailed");
        return null;
    }

    private bool TryRead(string path, out AppSettings? settings, out bool futureSchema, out bool failed, bool forImport = false)
    {
        settings = null;
        futureSchema = false;
        failed = false;
        try
        {
            var json = File.ReadAllText(path);
            if (forImport) ValidateImportDocument(json);
            settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions)
                ?? throw new JsonException("Settings must be a JSON object.");
            futureSchema = settings.SchemaVersion > CurrentSchemaVersion;
            if (futureSchema) throw new JsonException("Settings were created by a newer application version.");
            settings = Normalize(settings);
            return true;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            _logger.LogWarning(exception, "Settings could not be read from {Path}", path);
            settings = null;
            failed = true;
            return false;
        }
    }

    private static void ValidateImportDocument(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("Imported settings must be a JSON object.");
        JsonElement location = default;
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Name.Equals(nameof(AppSettings.LocationId), StringComparison.OrdinalIgnoreCase))
                location = property.Value;
            if (property.Name.Equals(nameof(AppSettings.SchemaVersion), StringComparison.OrdinalIgnoreCase)
                && (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out var version) || version < 1))
                throw new JsonException("Imported settings have an invalid schema version.");
        }
        if (location.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(location.GetString()))
            throw new JsonException("Imported settings must identify their selected location.");
    }

    private static void WriteAtomic(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private bool IsProfilePath(string path) => new[] { _path, _path + ".bak", _path + ".tmp", _path + ".bak.tmp" }
        .Contains(path, StringComparer.OrdinalIgnoreCase);

    private static bool IsStorageFailure(Exception exception) => exception is JsonException or IOException
        or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    private void SetStatus(string key, bool profile = false, bool clearTransfer = false)
    {
        var previous = StatusKey;
        if (profile) _profileStatusKey = key;
        else _transferStatusKey = key;
        if (clearTransfer) _transferStatusKey = "";
        if (!string.Equals(previous, StatusKey, StringComparison.OrdinalIgnoreCase))
            StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private AppSettings LoadDefaults()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(DefaultResourceName);
        if (stream is null)
        {
            _logger.LogError("Default settings resource {ResourceName} was not found", DefaultResourceName);
            return new AppSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(stream, _jsonOptions) ?? new AppSettings();
        }
        catch (JsonException exception)
        {
            _logger.LogError(exception, "Default settings JSON is invalid");
            return new AppSettings();
        }
    }

    private AppSettings Normalize(AppSettings settings)
    {
        var upgrading = settings.SchemaVersion < 4;
        settings.SchemaVersion = CurrentSchemaVersion;
        if (string.IsNullOrWhiteSpace(settings.LocationId)) settings.LocationId = "geonames-276781";
        if (string.IsNullOrWhiteSpace(settings.Language)) settings.Language = "en";
        if (string.IsNullOrWhiteSpace(settings.MethodId)) settings.MethodId = "MWL";
        settings.MethodId = CalculationMethod.Find(settings.MethodId).Id;
        if (!Enum.IsDefined(settings.AsrJuristic)) settings.AsrJuristic = AsrJuristic.Standard;
        if (!Enum.IsDefined(settings.HighLatitudeRule)) settings.HighLatitudeRule = HighLatitudeRule.AngleBased;
        settings.AudioVolume = Math.Clamp(settings.AudioVolume, 0, 100);
        settings.NotificationPauseMinutes = Math.Clamp(settings.NotificationPauseMinutes, 1, 1440);
        settings.SummerTimeOverrides = (settings.SummerTimeOverrides ?? [])
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value is not null)
            .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.OrdinalIgnoreCase);
        foreach (var preference in settings.SummerTimeOverrides.Values)
        {
            preference.AdjustmentMinutes = Math.Clamp(preference.AdjustmentMinutes, -180, 180);
            preference.Start ??= new() { Month = 3, Hour = 2 };
            preference.End ??= new() { Month = 10, Hour = 3 };
            NormalizeRule(preference.Start);
            NormalizeRule(preference.End);
        }
        settings.TextAdhkar ??= new();
        settings.TextAdhkar.DisplaySeconds = Math.Clamp(settings.TextAdhkar.DisplaySeconds, 5, 600);
        settings.TextAdhkar.PauseMinutes = Math.Clamp(settings.TextAdhkar.PauseMinutes, 1, 1440);
        settings.TextAdhkar.Texts = (settings.TextAdhkar.Texts ?? [])
            .Where(text => !string.IsNullOrWhiteSpace(text)).Select(text => text.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        // Only the exact old stock list is replaced; every user-edited list stays untouched.
        if (upgrading && settings.TextAdhkar.Texts.SequenceEqual(
            new[] { "سبحان الله", "الحمد لله", "الله أكبر" }, StringComparer.OrdinalIgnoreCase))
            settings.TextAdhkar.Texts.Clear();
        settings.Adhkar ??= new();
        settings.Adhkar.IntervalMinutes = Math.Clamp(settings.Adhkar.IntervalMinutes, 1, 1440);
        settings.Adhkar.AudioFiles = (settings.Adhkar.AudioFiles ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (upgrading && !settings.Adhkar.Enabled && settings.Adhkar.AudioFiles.Count == 0)
            settings.Adhkar.AudioFiles = BundledAudio.AdhkarFiles.ToList();
        settings.SavedLocations ??= [];
        if (settings.SavedLocations.Count > 0)
        {
            settings.SavedLocations = settings.SavedLocations.Where(place => place is not null
                && !string.IsNullOrWhiteSpace(place.Id)
                && _manualLocations.TryCreate(place.City, place.Latitude, place.Longitude, place.TimeZoneId,
                    place.CountryCode ?? "", out _, out _))
                .DistinctBy(place => place.Id, StringComparer.OrdinalIgnoreCase).ToList();
        }
        settings.HijriAdjustment = Math.Clamp(settings.HijriAdjustment, -2, 2);
        settings.PrayerNotifications = (settings.PrayerNotifications ?? [])
            .Where(pair => Enum.IsDefined(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var prayer in Enum.GetValues<Prayer>())
        {
            if (!settings.PrayerNotifications.TryGetValue(prayer, out var notification)
                || notification is null)
            {
                notification = new PrayerNotificationSettings
                {
                    NotifyBefore = prayer != Prayer.Sunrise,
                    NotifyAtTime = prayer != Prayer.Sunrise
                };
                settings.PrayerNotifications[prayer] = notification;
            }
            notification.MinutesBefore = Math.Clamp(notification.MinutesBefore, 1, 180);
            notification.MinutesAfter = Math.Clamp(notification.MinutesAfter, 1, 180);
            notification.AudioPath ??= "";
        }
        settings.MinuteAdjustments = NormalizeAdjustments(settings.MinuteAdjustments);
        settings.LocationMinuteAdjustments = (settings.LocationMinuteAdjustments ?? [])
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
            .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => NormalizeAdjustments(group.Last().Value), StringComparer.OrdinalIgnoreCase);
        // Legacy corrections belong only to the location that was selected when they were saved.
        if (!settings.LocationMinuteAdjustments.ContainsKey(settings.LocationId))
            settings.LocationMinuteAdjustments[settings.LocationId] = new(settings.MinuteAdjustments);
        settings.MinuteAdjustments = new(settings.LocationMinuteAdjustments[settings.LocationId]);
        settings.RecentPrayerAlerts = (settings.RecentPrayerAlerts ?? [])
            .Where(key => !string.IsNullOrWhiteSpace(key)).Distinct(StringComparer.OrdinalIgnoreCase).TakeLast(128).ToList();
        return settings;
    }

    private static Dictionary<Prayer, int> NormalizeAdjustments(Dictionary<Prayer, int>? adjustments) => (adjustments ?? [])
        .Where(pair => Enum.IsDefined(pair.Key))
        .ToDictionary(pair => pair.Key, pair => Math.Clamp(pair.Value, -60, 60));

    private static void NormalizeRule(SummerTimeRule rule)
    {
        rule.Month = Math.Clamp(rule.Month, 1, 12);
        rule.Day = Math.Clamp(rule.Day, 1, 31);
        rule.Occurrence = Math.Clamp(rule.Occurrence, 1, 5);
        if (!Enum.IsDefined(rule.Weekday)) rule.Weekday = DayOfWeek.Sunday;
        rule.Hour = Math.Clamp(rule.Hour, 0, 23);
        rule.Minute = Math.Clamp(rule.Minute, 0, 59);
    }
}
