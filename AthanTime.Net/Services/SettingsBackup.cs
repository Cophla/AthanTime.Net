using System.Text.Json;
using System.Text.Json.Nodes;
using AthanTime.Net.Models;
using Microsoft.Extensions.Logging;

namespace AthanTime.Net.Services;

/// <summary>A validated settings backup awaiting user confirmation.</summary>
public sealed record SettingsBackup(AppSettings Settings, MovementBreakPreferences? Movement, string Section);

public sealed partial class SettingsService
{
    private const string BackupFormat = "AthanTime.SettingsBackup";
    private const int BackupSizeLimit = 16 * 1024 * 1024;
    private static readonly Dictionary<string, string[]> _backupSections = new(StringComparer.Ordinal)
    {
        ["Location"] = [nameof(AppSettings.LocationId), nameof(AppSettings.LocationNeedsConfirmation), nameof(AppSettings.SavedLocations)],
        ["PrayerClock"] = [nameof(AppSettings.UseAutomaticConvention), nameof(AppSettings.MethodId), nameof(AppSettings.AsrJuristic),
            nameof(AppSettings.HighLatitudeRule), nameof(AppSettings.Use24HourClock), nameof(AppSettings.SummerTimeOverrides), nameof(AppSettings.HijriAdjustment),
            nameof(AppSettings.LocationMinuteAdjustments), nameof(AppSettings.NotificationsEnabled), nameof(AppSettings.NotificationPauseMinutes)],
        ["AudioAdhkar"] = [nameof(AppSettings.PrayerAudioEnabled), nameof(AppSettings.AudioVolume), nameof(AppSettings.PrayerNotifications), nameof(AppSettings.Adhkar), nameof(AppSettings.TextAdhkar)],
        ["PrayerReminders"] = [nameof(AppSettings.PrayerNotifications)],
        ["Adhkar"] = [nameof(AppSettings.Adhkar)],
        ["TextAdhkar"] = [nameof(AppSettings.TextAdhkar)],
        ["General"] = [nameof(AppSettings.Language), nameof(AppSettings.UseDarkMode), nameof(AppSettings.StartMinimized),
            nameof(AppSettings.CloseToTray), nameof(AppSettings.ReadingHighlightSpeed)]
    };

    /// <inheritdoc />
    public bool ExportBackup(AppSettings settings, MovementBreakPreferences? movement, string section, string path)
    {
        try
        {
            var destination = Path.GetFullPath(path);
            if (IsProfilePath(destination)
                || destination.Equals(Path.Combine(Path.GetDirectoryName(_path)!, "window-position.json"), StringComparison.OrdinalIgnoreCase)
                || !Path.GetExtension(destination).Equals(".json", StringComparison.OrdinalIgnoreCase))
                throw new IOException("Select a JSON backup path outside the active profile files.");
            if (settings.SchemaVersion > CurrentSchemaVersion) throw new JsonException("Unsupported settings version.");
            var values = JsonSerializer.SerializeToNode(settings, _jsonOptions)!.AsObject();
            if (section is "All" or "PrayerClock")
            {
                var snapshot = Normalize(values.Deserialize<AppSettings>(_jsonOptions)!);
                snapshot.MinuteAdjustments = NormalizeAdjustments(settings.MinuteAdjustments);
                snapshot.LocationMinuteAdjustments[snapshot.LocationId] = snapshot.MinuteAdjustments;
                values = JsonSerializer.SerializeToNode(snapshot, _jsonOptions)!.AsObject();
            }
            if (section == "All")
            {
                if (movement is null) throw new InvalidDataException("Movement preferences are unavailable.");
                movement = MovementBreakRepository.Normalize(movement);
                foreach (var key in new[] { nameof(AppSettings.StartOnLogin), nameof(AppSettings.LoggingEnabled), nameof(AppSettings.DebugLoggingEnabled),
                    nameof(AppSettings.RecentPrayerAlerts), nameof(AppSettings.NotificationsPausedUntilUtc) }) values.Remove(key);
            }
            else
            {
                var sectionKeys = GetBackupKeys(section);
                values = new JsonObject(sectionKeys.Select(key =>
                    new KeyValuePair<string, JsonNode?>(key, values[key]?.DeepClone())));
            }
            var json = JsonSerializer.Serialize(new BackupDocument(BackupFormat, 1, section, values, movement), _jsonOptions);
            if (System.Text.Encoding.UTF8.GetByteCount(json) > BackupSizeLimit)
                throw new InvalidDataException("The settings backup exceeds the size limit.");
            WriteAtomic(destination, json);
            SetStatus("SettingsExported");
            return true;
        }
        catch (Exception exception) when (IsStorageFailure(exception) || exception is InvalidDataException or InvalidOperationException)
        {
            _logger.LogWarning(exception, "Could not export settings backup for {Section}", section);
            SetStatus("SettingsExportFailed");
            return false;
        }
    }

    /// <inheritdoc />
    public SettingsBackup? ReadBackup(AppSettings current, string section, string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            if (file.Length > BackupSizeLimit) throw new InvalidDataException("Settings backup is too large.");
            var root = JsonNode.Parse(file) as JsonObject ?? throw new JsonException("Expected a JSON object.");
            var legacy = !root.ContainsKey("Format") && section == "All";
            var document = legacy
                ? new BackupDocument(BackupFormat, 1, section, root, null)
                : root.Deserialize<BackupDocument>(_jsonOptions);
            if (document is null || document.Format != BackupFormat || document.Version != 1
                || document.Values is null)
                throw new JsonException("This file is for another section or backup version.");
            // The three older audio subsection backups still restore through their parent scope.
            if (document.Section != section && !(section == "AudioAdhkar"
                && document.Section is "PrayerReminders" or "Adhkar" or "TextAdhkar"))
                throw new JsonException("This file is for another settings section.");
            section = document.Section;

            var values = document.Values;
            MovementBreakPreferences? movement = null;
            if (section == "All")
            {
                ValidateImportDocument(values.ToJsonString());
                if (!legacy && (!values.ContainsKey(nameof(AppSettings.SchemaVersion))
                    || !HasRequiredBackupKeys(values, _backupSections.Values.SelectMany(keys => keys))))
                    throw new JsonException("The complete backup is missing settings.");
                if (document.Movement is { } preferences)
                {
                    if (root["Movement"] is not JsonObject movementValues
                        || !movementValues.ContainsKey(nameof(MovementBreakPreferences.Enabled))
                        || !movementValues.ContainsKey(nameof(MovementBreakPreferences.IntervalMinutes))
                        || !movementValues.ContainsKey(nameof(MovementBreakPreferences.DisplaySeconds))
                        || !movementValues.ContainsKey(nameof(MovementBreakPreferences.Practices)))
                        throw new JsonException("The complete backup is missing movement preferences.");
                    if (preferences.IntervalMinutes is < 1 or > 1440 || preferences.DisplaySeconds is < 5 or > 600)
                        throw new InvalidDataException("Invalid movement timing.");
                    movement = MovementBreakRepository.Normalize(preferences);
                }
                else if (!legacy) throw new JsonException("The complete backup requires movement settings.");
            }
            else
            {
                var keys = GetBackupKeys(section);
                if (!HasRequiredBackupKeys(values, keys) || values.Any(pair => !keys.Contains(pair.Key))
                    || document.Movement is not null)
                    throw new JsonException("Incomplete settings section.");
                var merged = JsonSerializer.SerializeToNode(current, _jsonOptions)!.AsObject();
                foreach (var pair in values) merged[pair.Key] = pair.Value?.DeepClone();
                // Keep the selected city's active adjustments derived from the per-city map.
                if (section is "Location" or "PrayerClock") merged.Remove(nameof(AppSettings.MinuteAdjustments));
                values = merged;
            }
            var imported = values.Deserialize<AppSettings>(_jsonOptions) ?? throw new JsonException("Settings are missing.");
            if (imported.SchemaVersion > CurrentSchemaVersion) throw new JsonException("Unsupported settings version.");
            if (section == "All" && !values.Any(pair => pair.Key.Equals(nameof(AppSettings.ReadingHighlightSpeed), StringComparison.OrdinalIgnoreCase)))
                imported.ReadingHighlightSpeed = current.ReadingHighlightSpeed;
            SetStatus("");
            return new SettingsBackup(Normalize(imported), movement, section);
        }
        catch (Exception exception) when (IsStorageFailure(exception) || exception is InvalidDataException or InvalidOperationException)
        {
            _logger.LogWarning(exception, "Could not read settings backup for {Section}", section);
            SetStatus("SettingsImportFailed");
            return null;
        }
    }

    private static string[] GetBackupKeys(string section) => _backupSections.TryGetValue(section, out var keys)
        ? keys : throw new JsonException("Unknown settings section.");

    // Backups created before the speed setting keep the device's current reading pace.
    private static bool HasRequiredBackupKeys(JsonObject values, IEnumerable<string> keys) =>
        keys.All(key => key == nameof(AppSettings.ReadingHighlightSpeed) || values.ContainsKey(key));

    private sealed record BackupDocument(string Format, int Version, string Section, JsonObject Values, MovementBreakPreferences? Movement);
}
