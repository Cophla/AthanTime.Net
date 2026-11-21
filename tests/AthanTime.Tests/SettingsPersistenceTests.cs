using System.Text.Json;
using AthanTime.Net.Models;
using AthanTime.Net.Services;
using Microsoft.Extensions.Logging;

namespace AthanTime.Tests;

/// <summary>Tests real per-user JSON persistence in isolated temporary directories.</summary>
public sealed class SettingsPersistenceTests(ILogger<SettingsService> logger, ManualLocationService manual) : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("athan-settings-tests-").FullName;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FirstLaunchDoesNotThrowForAbsentProfileFiles(bool createProfileDirectory)
    {
        var profile = Path.Combine(_directory, "fresh");
        if (createProfileDirectory) Directory.CreateDirectory(profile);
        var settingsPath = Path.Combine(profile, "settings.json");
        var positionPath = Path.Combine(profile, "window-position.json");
        var settings = new SettingsService(logger, manual, settingsPath,
            firstRun => firstRun.LocationNeedsConfirmation = true);
        var position = new WindowPositionService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<WindowPositionService>.Instance, positionPath);
        var observed = 0;
        var thread = Environment.CurrentManagedThreadId;
        EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs> handler = (_, args) =>
        {
            if (Environment.CurrentManagedThreadId == thread &&
                args.Exception is FileNotFoundException or DirectoryNotFoundException) observed++;
        };
        AppDomain.CurrentDomain.FirstChanceException += handler;
        try
        {
            Assert.True(settings.Load().LocationNeedsConfirmation);
            Assert.Equal("", settings.StatusKey);
            Assert.Null(position.Load());
        }
        finally { AppDomain.CurrentDomain.FirstChanceException -= handler; }
        Assert.Equal(0, observed);
        Assert.Equal(createProfileDirectory, Directory.Exists(profile));
        if (createProfileDirectory) Assert.Empty(Directory.EnumerateFileSystemEntries(profile));
    }

    [Fact]
    public void DirectoryAtPrimaryPathIsAProfileErrorRatherThanFirstRun()
    {
        var path = Path.Combine(_directory, "settings.json");
        Directory.CreateDirectory(path);
        var store = new SettingsService(logger, manual, path,
            firstRun => firstRun.LocationNeedsConfirmation = true);
        Assert.False(store.Load().LocationNeedsConfirmation);
        Assert.Equal("SettingsLoadFailed", store.StatusKey);
    }

    [Theory]
    [InlineData("General")]
    [InlineData("All")]
    public void ReadingSpeedRoundTripsAndOlderBackupsKeepTheCurrentSpeed(string section)
    {
        var store = new SettingsService(logger, manual, Path.Combine(_directory, "settings.json"));
        var path = Path.Combine(_directory, "reading.json");
        var settings = store.Load();
        Assert.Equal(8, settings.ReadingHighlightSpeed);
        settings.ReadingHighlightSpeed = 24;
        store.Save(settings);
        settings = store.Load();
        Assert.Equal(20, settings.ReadingHighlightSpeed);
        Assert.True(store.ExportBackup(settings, section == "All" ? new MovementBreakPreferences() : null, section, path));
        var current = new AppSettings { ReadingHighlightSpeed = 11 };
        Assert.Equal(20, store.ReadBackup(current, section, path)!.Settings.ReadingHighlightSpeed);

        var older = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        older["Values"]!.AsObject().Remove(nameof(AppSettings.ReadingHighlightSpeed));
        File.WriteAllText(path, older.ToJsonString());
        Assert.Equal(11, store.ReadBackup(current, section, path)!.Settings.ReadingHighlightSpeed);
    }

    [Theory]
    [InlineData("Location", "SavedLocations", "AudioVolume")]
    [InlineData("PrayerClock", "Use24HourClock", "LocationId")]
    [InlineData("AudioAdhkar", "AudioVolume", "UseDarkMode")]
    [InlineData("PrayerReminders", "PrayerNotifications", "Adhkar")]
    [InlineData("Adhkar", "Adhkar", "TextAdhkar")]
    [InlineData("TextAdhkar", "TextAdhkar", "Adhkar")]
    [InlineData("General", "UseDarkMode", "AudioVolume")]
    public void SectionBackupsContainOnlyTheirOwnPreferences(string section, string included, string excluded)
    {
        var path = Path.Combine(_directory, "settings.json");
        var backup = Path.Combine(_directory, "section.json");
        var store = new SettingsService(logger, manual, path);
        var current = store.Load();
        current.AudioVolume = 19;
        current.TextAdhkar.DisplaySeconds = 27;
        var source = store.Load();
        source.UseDarkMode = true;
        source.AudioVolume = 88;
        source.TextAdhkar.DisplaySeconds = 5;
        Assert.True(store.ExportBackup(source, null, section, backup));
        using var json = JsonDocument.Parse(File.ReadAllText(backup));
        var values = json.RootElement.GetProperty("Values");
        Assert.True(values.TryGetProperty(included, out _));
        Assert.False(values.TryGetProperty(excluded, out _));
        Assert.Null(store.ReadBackup(current, "All", backup));
        var restored = Assert.IsType<SettingsBackup>(store.ReadBackup(current, section, backup)).Settings;
        Assert.Equal(section == "AudioAdhkar" ? 88 : 19, restored.AudioVolume);
        Assert.Equal(section is "AudioAdhkar" or "TextAdhkar" ? 5 : 27, restored.TextAdhkar.DisplaySeconds);
        Assert.Equal(section == "General", restored.UseDarkMode);
        if (section is "PrayerReminders" or "Adhkar" or "TextAdhkar")
        {
            var olderAudio = Assert.IsType<SettingsBackup>(store.ReadBackup(current, "AudioAdhkar", backup));
            Assert.Equal(section, olderAudio.Section);
            Assert.Equal(19, olderAudio.Settings.AudioVolume);
            Assert.Equal(section == "TextAdhkar" ? 5 : 27, olderAudio.Settings.TextAdhkar.DisplaySeconds);
        }
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void WholeBackupCarriesMovementAndCanStillReadLegacySettingsJson()
    {
        var store = new SettingsService(logger, manual, Path.Combine(_directory, "settings.json"));
        var backup = Path.Combine(_directory, "backup.json");
        var settings = store.Load();
        Assert.Equal(5, settings.TextAdhkar.DisplaySeconds);
        settings.StartOnLogin = true;
        settings.TextAdhkar.Texts = ["My text"];
        var movement = new MovementBreakPreferences
        {
            Enabled = true,
            Practices = [new("custom", new Dictionary<string, string> { ["fr"] = "Bouger", ["en"] = "Move" })]
        };
        Assert.True(store.ExportBackup(settings, movement, "All", backup));
        var restored = Assert.IsType<SettingsBackup>(store.ReadBackup(new(), "All", backup));
        Assert.False(restored.Settings.StartOnLogin);
        Assert.Equal("My text", Assert.Single(restored.Settings.TextAdhkar.Texts));
        Assert.Equal(20, restored.Movement!.DisplaySeconds);
        Assert.Equal("Bouger", Assert.Single(restored.Movement.Practices).GetText("fr"));
        File.WriteAllText(backup, JsonSerializer.Serialize(settings));
        Assert.Null(Assert.IsType<SettingsBackup>(store.ReadBackup(new(), "All", backup)).Movement);
    }

    [Fact]
    public void RemovedBundledTahmidIsFilteredWhileCustomRecordingsAreRetained()
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = new SettingsService(logger, manual, path);
        var settings = store.Load();
        var custom = Path.Combine(_directory, "alhamdulillah.ogg");
        settings.Adhkar.AudioFiles = ["BUNDLED:ALHAMDULILLAH", "bundled:takbeer", custom];
        // Write a legacy profile directly so Load exercises its normal compatibility path.
        File.WriteAllText(path, JsonSerializer.Serialize(settings));
        Assert.Equal(new[] { "bundled:takbeer", custom }, store.Load().Adhkar.AudioFiles);
        Assert.Equal(new[] { "bundled:takbeer" }, new AdhkarSettings().AudioFiles);
    }

    /// <summary>Saving any combination of switches retains each separate before/after duration.</summary>
    [Theory]
    [InlineData(Prayer.Dhuhr, false, false, false)]
    [InlineData(Prayer.Dhuhr, false, false, true)]
    [InlineData(Prayer.Dhuhr, false, true, false)]
    [InlineData(Prayer.Dhuhr, false, true, true)]
    [InlineData(Prayer.Dhuhr, true, false, false)]
    [InlineData(Prayer.Dhuhr, true, false, true)]
    [InlineData(Prayer.Dhuhr, true, true, false)]
    [InlineData(Prayer.Dhuhr, true, true, true)]
    [InlineData(Prayer.Sunrise, false, false, false)]
    [InlineData(Prayer.Sunrise, false, false, true)]
    [InlineData(Prayer.Sunrise, false, true, false)]
    [InlineData(Prayer.Sunrise, false, true, true)]
    [InlineData(Prayer.Sunrise, true, false, false)]
    [InlineData(Prayer.Sunrise, true, false, true)]
    [InlineData(Prayer.Sunrise, true, true, false)]
    [InlineData(Prayer.Sunrise, true, true, true)]
    public void NotificationSwitchesAndDurations_RoundTripIndependently(Prayer prayer, bool before, bool atTime, bool after)
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = new SettingsService(logger, manual, path);
        var settings = store.Load();
        var row = settings.PrayerNotifications[prayer];
        row.NotifyBefore = before; row.NotifyAtTime = atTime; row.NotifyAfter = after;
        row.MinutesBefore = 30; row.MinutesAfter = 10;
        store.Save(settings);
        var loaded = new SettingsService(logger, manual, path).Load();
        var saved = loaded.PrayerNotifications[prayer];
        Assert.Equal(before, saved.NotifyBefore);
        Assert.Equal(atTime, saved.NotifyAtTime);
        Assert.Equal(after, saved.NotifyAfter);
        Assert.Equal(30, saved.MinutesBefore);
        Assert.Equal(10, saved.MinutesAfter);
        Assert.Equal(15, loaded.PrayerNotifications[Prayer.Fajr].MinutesBefore);
        Assert.Equal(5, loaded.PrayerNotifications[Prayer.Fajr].MinutesAfter);
    }

    /// <summary>Existing choices, including the previous ten-minute default, are never overwritten.</summary>
    [Theory]
    [InlineData(10)]
    [InlineData(15)]
    [InlineData(30)]
    public void BeforePrayerMinutes_PersistAcrossReload(int minutes)
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = new SettingsService(logger, manual, path);
        var settings = store.Load();
        settings.PrayerNotifications[Prayer.Dhuhr].NotifyBefore = true;
        settings.PrayerNotifications[Prayer.Dhuhr].MinutesBefore = minutes;
        store.Save(settings);
        var loaded = new SettingsService(logger, manual, path).Load().PrayerNotifications[Prayer.Dhuhr];
        Assert.True(loaded.NotifyBefore);
        Assert.Equal(minutes, loaded.MinutesBefore);
    }

    /// <summary>Missing rows and omitted advance-minute fields acquire the fifteen-minute default.</summary>
    [Theory]
    [InlineData("""{"schemaVersion":5}""")]
    [InlineData("""{"schemaVersion":5,"prayerNotifications":{"Dhuhr":{}}}""")]
    public void MissingBeforePrayerSettings_DefaultToFifteenMinutes(string json)
    {
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, json);
        var settings = new SettingsService(logger, manual, path).Load();
        Assert.All(settings.PrayerNotifications.Values, row => Assert.Equal(15, row.MinutesBefore));
    }

    [Fact]
    public void NewSettings_RoundTripLocationsPerPrayerAudioPlaylistAndTheme()
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = new SettingsService(logger, manual, path);
        var settings = store.Load();
        settings.UseDarkMode = true;
        settings.SummerTimeOverrides["Asia/Beirut"] = new() { Automatic = false, Enabled = true, AdjustmentMinutes = 30,
            UseCustomSchedule = true, Start = new() { Month = 4, Occurrence = 1, Weekday = DayOfWeek.Thursday, Hour = 0, Minute = 30 },
            End = new() { Month = 9, UseWeekday = false, Day = 15, Hour = 23, Minute = 45 } };
        settings.SavedLocations.Add(new("manual-sidon", "Sidon custom", "LB", 33.5631, 35.3689, "Asia/Beirut"));
        settings.LocationId = "manual-sidon";
        settings.PrayerNotifications[Prayer.Fajr].AudioEnabled = true;
        settings.PrayerNotifications[Prayer.Fajr].AudioPath = "/audio/fajr.ogg";
        settings.PrayerNotifications[Prayer.Sunrise].MinutesBefore = 17;
        settings.Adhkar = new() { Enabled = true, RandomOrder = true, IntervalMinutes = 13, AudioFiles = ["/audio/one.ogg", "/audio/two.ogg"] };
        settings.TextAdhkar = new() { Enabled = true, DisplaySeconds = 45, PauseMinutes = 12, Texts = ["سبحان الله", "Custom text"] };
        store.Save(settings);
        var loaded = new SettingsService(logger, manual, path).Load();
        Assert.Equal(5, loaded.SchemaVersion);
        Assert.True(loaded.UseDarkMode);
        Assert.False(loaded.SummerTimeOverrides["asia/beirut"].Automatic);
        Assert.True(loaded.SummerTimeOverrides["asia/beirut"].Enabled);
        Assert.Equal(30, loaded.SummerTimeOverrides["asia/beirut"].AdjustmentMinutes);
        var summer = loaded.SummerTimeOverrides["asia/beirut"];
        Assert.True(summer.UseCustomSchedule);
        Assert.Equal(4, summer.Start.Month); Assert.Equal(1, summer.Start.Occurrence);
        Assert.Equal(DayOfWeek.Thursday, summer.Start.Weekday); Assert.Equal(30, summer.Start.Minute);
        Assert.False(summer.End.UseWeekday); Assert.Equal(15, summer.End.Day);
        Assert.Equal(23, summer.End.Hour); Assert.Equal(45, summer.End.Minute);
        Assert.Equal("manual-sidon", loaded.LocationId);
        Assert.Equal(33.5631, Assert.Single(loaded.SavedLocations).Latitude);
        Assert.True(loaded.PrayerNotifications[Prayer.Fajr].AudioEnabled);
        Assert.Equal("/audio/fajr.ogg", loaded.PrayerNotifications[Prayer.Fajr].AudioPath);
        Assert.Equal(17, loaded.PrayerNotifications[Prayer.Sunrise].MinutesBefore);
        Assert.True(loaded.Adhkar.RandomOrder);
        Assert.Equal(13, loaded.Adhkar.IntervalMinutes);
        Assert.Equal(settings.Adhkar.AudioFiles, loaded.Adhkar.AudioFiles);
        Assert.True(loaded.TextAdhkar.Enabled);
        Assert.Equal(45, loaded.TextAdhkar.DisplaySeconds);
        Assert.Equal(12, loaded.TextAdhkar.PauseMinutes);
        Assert.Equal(settings.TextAdhkar.Texts, loaded.TextAdhkar.Texts);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void InvalidOrMissingAnnualRuleFieldsAreNormalizedWithoutChangingOldMode()
    {
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, """{"summerTimeOverrides":{"Etc/UTC":{"automatic":false,"enabled":true,"start":{"month":99,"day":0,"occurrence":999,"weekday":99,"hour":44,"minute":-1},"end":null}}}""");
        var rule = new SettingsService(logger, manual, path).Load().SummerTimeOverrides["etc/utc"];
        Assert.False(rule.UseCustomSchedule);
        Assert.True(rule.Enabled);
        Assert.Equal(12, rule.Start.Month); Assert.Equal(1, rule.Start.Day);
        Assert.Equal(5, rule.Start.Occurrence); Assert.Equal(DayOfWeek.Sunday, rule.Start.Weekday);
        Assert.Equal(23, rule.Start.Hour); Assert.Equal(0, rule.Start.Minute);
        Assert.Equal(10, rule.End.Month);
    }

    [Fact]
    public void VersionTwoAndCaseInsensitiveJson_LoadWithSafeNewDefaults()
    {
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, """{"SCHEMAVERSION":2,"LANGUAGE":"AR","METHODID":"egypt","AudioVolume":500,"Adhkar":{"IntervalMinutes":0,"AudioFiles":["A.mp3","a.MP3"]},"PrayerNotifications":{"sunrise":{"MinutesBefore":900,"MinutesAfter":0}},"MinuteAdjustments":{"maghrib":5}}""");
        var loaded = new SettingsService(logger, manual, path).Load();
        Assert.Equal(5, loaded.SchemaVersion);
        Assert.Equal("Egypt", loaded.MethodId);
        Assert.Equal(6, loaded.PrayerNotifications.Count);
        Assert.Equal(100, loaded.AudioVolume);
        Assert.Equal(1, loaded.Adhkar.IntervalMinutes);
        Assert.Single(loaded.Adhkar.AudioFiles);
        Assert.Equal(180, loaded.PrayerNotifications[Prayer.Sunrise].MinutesBefore);
        Assert.Equal(1, loaded.PrayerNotifications[Prayer.Sunrise].MinutesAfter);
        Assert.Equal(5, loaded.MinuteAdjustments[Prayer.Maghrib]);
        Assert.False(loaded.PrayerNotifications[Prayer.Fajr].AudioEnabled);
    }

    [Fact]
    public void MalformedJson_ReturnsDefaultsWithoutCrashing()
    {
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, "{broken");
        var store = new SettingsService(logger, manual, path);
        var loaded = store.Load();
        Assert.Equal(6, loaded.PrayerNotifications.Count);
        Assert.False(loaded.Adhkar.Enabled);
        Assert.Equal("SettingsLoadFailed", store.StatusKey);
        Assert.Equal("{broken", File.ReadAllText(path));
    }

    /// <summary>Recovery keeps malformed primary data from contaminating the backup.</summary>
    [Fact]
    public void DamagedPrimary_RecoversBackupAndNeverCopiesDamageOverIt()
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = new SettingsService(logger, manual, path);
        var settings = store.Load();
        settings.Language = "ar";
        store.Save(settings);
        Assert.True(File.Exists(path + ".bak"));
        settings.Language = "fr";
        store.Save(settings);
        var backup = File.ReadAllText(path + ".bak");
        File.WriteAllText(path, "{damaged");
        var statuses = new List<string>();
        store.StatusChanged += (_, _) => statuses.Add(store.StatusKey);

        var recovered = store.Load();

        Assert.Equal("ar", recovered.Language);
        Assert.Equal("SettingsRecovered", store.StatusKey);
        Assert.Contains("SettingsRecovered", statuses);
        Assert.Equal("{damaged", File.ReadAllText(path));
        store.Save(recovered);
        Assert.Equal(backup, File.ReadAllText(path + ".bak"));
        Assert.Equal("ar", new SettingsService(logger, manual, path).Load().Language);
        Assert.False(File.Exists(path + ".tmp"));
        Assert.False(File.Exists(path + ".bak.tmp"));
    }

    /// <summary>A backup also covers a missing primary and is rebuilt only after an explicit save.</summary>
    [Fact]
    public void MissingPrimary_UsesBackupAndDamagedBackupCanBeRebuiltAfterExplicitSave()
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = new SettingsService(logger, manual, path);
        var settings = store.Load();
        settings.UseDarkMode = true;
        store.Save(settings);
        File.Delete(path);
        Assert.True(store.Load().UseDarkMode);
        Assert.Equal("SettingsRecovered", store.StatusKey);
        File.WriteAllText(path + ".bak", "null");
        var defaults = store.Load();
        Assert.Equal("SettingsLoadFailed", store.StatusKey);
        store.Save(defaults);
        File.Delete(path);
        Assert.Equal(defaults.LocationId, store.Load().LocationId);
        Assert.Equal("SettingsRecovered", store.StatusKey);
    }

    /// <summary>Failures remain visible and exports cannot overwrite the active settings files.</summary>
    [Fact]
    public void FailedSaveAndExport_AreVisibleAndDoNotCorruptExistingProfile()
    {
        var blockedPath = Path.Combine(_directory, "blocked.json");
        Directory.CreateDirectory(blockedPath);
        var store = new SettingsService(logger, manual, blockedPath);
        store.Save(new());
        Assert.Equal("SettingsSaveFailed", store.StatusKey);
        Assert.True(Directory.Exists(blockedPath));
        Assert.False(File.Exists(blockedPath + ".tmp"));

        var goodPath = Path.Combine(_directory, "settings.json");
        store = new SettingsService(logger, manual, goodPath);
        store.Save(new());
        var original = File.ReadAllText(goodPath);
        Assert.False(ExportWholeBackup(store, new() { Language = "ar" }, goodPath));
        Assert.Equal("SettingsExportFailed", store.StatusKey);
        Assert.Equal(original, File.ReadAllText(goodPath));
        Assert.False(ExportWholeBackup(store, new(), blockedPath));
        Assert.Equal("SettingsExportFailed", store.StatusKey);
        Assert.False(File.Exists(blockedPath + ".tmp"));
    }

    /// <summary>Transfers use detached snapshots and require a separate apply action.</summary>
    [Fact]
    public void ImportExport_ValidateSnapshotsWithoutApplyingOrTouchingProfile()
    {
        var path = Path.Combine(_directory, "settings.json");
        var exportedPath = Path.Combine(_directory, "transfers", "export.json");
        var store = new SettingsService(logger, manual, path);
        var settings = store.Load();
        store.Save(settings);
        var primary = File.ReadAllText(path);
        var backup = File.ReadAllText(path + ".bak");
        settings.Language = "AR";
        settings.MinuteAdjustments[Prayer.Maghrib] = 5;

        Assert.True(ExportWholeBackup(store, settings, exportedPath));
        Assert.Equal("SettingsExported", store.StatusKey);
        var imported = ReadWholeBackup(store, exportedPath);

        Assert.NotNull(imported);
        Assert.Equal("AR", imported.Language);
        Assert.Equal(5, imported.MinuteAdjustments[Prayer.Maghrib]);
        Assert.Equal(5, imported.LocationMinuteAdjustments[imported.LocationId][Prayer.Maghrib]);
        Assert.Equal(primary, File.ReadAllText(path));
        Assert.Equal(backup, File.ReadAllText(path + ".bak"));
        imported.Language = "de";
        Assert.Equal("AR", settings.Language);
        Assert.False(File.Exists(exportedPath + ".tmp"));
    }

    /// <summary>Malformed and newer schemas are rejected rather than silently converted to defaults.</summary>
    [Theory]
    [InlineData("{damaged")]
    [InlineData("null")]
    [InlineData("{\"SchemaVersion\":999,\"LocationId\":\"geonames-276781\"}")]
    public void BadOrFutureImports_AreRejectedAndNeverModifyTheProfile(string json)
    {
        var path = Path.Combine(_directory, "settings.json");
        var importPath = Path.Combine(_directory, "import.json");
        var store = new SettingsService(logger, manual, path);
        store.Save(new());
        var original = File.ReadAllText(path);
        File.WriteAllText(importPath, json);

        Assert.Null(ReadWholeBackup(store, importPath));

        Assert.Equal("SettingsImportFailed", store.StatusKey);
        Assert.Equal(original, File.ReadAllText(path));
        Assert.Equal(json, File.ReadAllText(importPath));
    }

    /// <summary>Unrelated JSON and malformed schema metadata are not interpreted as replacement defaults.</summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"Application\":{\"SupportEmail\":\"athantimedotnet@gmail.com\"}}")]
    [InlineData("{\"LocationId\":\"\"}")]
    [InlineData("{\"LocationId\":null}")]
    [InlineData("{\"LocationId\":7}")]
    [InlineData("{\"LocationId\":\"geonames-276781\",\"SchemaVersion\":0}")]
    [InlineData("{\"LocationId\":\"geonames-276781\",\"SchemaVersion\":-1}")]
    [InlineData("{\"LocationId\":\"geonames-276781\",\"SchemaVersion\":\"5\"}")]
    [InlineData("{\"LocationId\":\"geonames-276781\",\"SchemaVersion\":null}")]
    public void UnrelatedOrInvalidImportMetadata_IsRejected(string json)
    {
        var path = Path.Combine(_directory, "import.json");
        File.WriteAllText(path, json);
        var store = new SettingsService(logger, manual, Path.Combine(_directory, "settings.json"));

        Assert.Null(ReadWholeBackup(store, path));
        Assert.Equal("SettingsImportFailed", store.StatusKey);
        Assert.False(File.Exists(Path.Combine(_directory, "settings.json")));
    }

    /// <summary>Legacy location-based profiles remain importable without schema metadata.</summary>
    [Fact]
    public void LegacyImportWithoutSchema_IsAcceptedCaseInsensitively()
    {
        var path = Path.Combine(_directory, "import.json");
        File.WriteAllText(path, """{"LOCATIONID":"geonames-276781","minuteAdjustments":{"maghrib":5}}""");
        var store = new SettingsService(logger, manual, Path.Combine(_directory, "settings.json"));
        var settings = ReadWholeBackup(store, path);

        Assert.NotNull(settings);
        Assert.Equal(5, settings.SchemaVersion);
        Assert.Equal(5, settings.MinuteAdjustments[Prayer.Maghrib]);
    }

    /// <summary>Validating or exporting a transfer cannot dismiss an unresolved profile failure.</summary>
    [Fact]
    public void TransferResults_DoNotHideStickyProfileWarnings()
    {
        var blockedPath = Path.Combine(_directory, "blocked");
        Directory.CreateDirectory(blockedPath);
        var exportPath = Path.Combine(_directory, "export.json");
        var store = new SettingsService(logger, manual, blockedPath);
        store.Save(new());
        Assert.Equal("SettingsSaveFailed", store.StatusKey);

        Assert.True(ExportWholeBackup(store, new(), exportPath));
        Assert.Equal("SettingsSaveFailed", store.StatusKey);
        Assert.NotNull(ReadWholeBackup(store, exportPath));
        Assert.Equal("SettingsSaveFailed", store.StatusKey);
        Assert.Null(ReadWholeBackup(store, Path.Combine(_directory, "missing.json")));
        Assert.Equal("SettingsSaveFailed", store.StatusKey);

        Directory.Delete(blockedPath);
        store.Save(new());
        Assert.Equal("SettingsImportFailed", store.StatusKey);
        Assert.NotNull(ReadWholeBackup(store, exportPath));
        Assert.Equal("", store.StatusKey);
    }

    /// <summary>Backup recovery remains visible until a fresh healthy profile is loaded.</summary>
    [Fact]
    public void RecoveryWarning_RemainsAcrossTransfersAndSuccessfulSaves()
    {
        var path = Path.Combine(_directory, "settings.json");
        var exported = Path.Combine(_directory, "export.json");
        var store = new SettingsService(logger, manual, path);
        store.Save(new());
        File.WriteAllText(path, "{broken");
        var recovered = store.Load();

        Assert.True(ExportWholeBackup(store, recovered, exported));
        Assert.NotNull(ReadWholeBackup(store, exported));
        store.Save(recovered);
        Assert.Equal("SettingsRecovered", store.StatusKey);
        store.Load();
        Assert.Equal("", store.StatusKey);
    }

    /// <summary>Older app versions cannot replace a newer application's profile.</summary>
    [Fact]
    public void FutureProfile_IsNeverSilentlyDowngradedEvenWhenBackupExists()
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = new SettingsService(logger, manual, path);
        store.Save(new() { Language = "ar" });
        File.WriteAllText(path, "{\"SchemaVersion\":999,\"Language\":\"de\"}");
        var future = File.ReadAllText(path);
        var backup = File.ReadAllText(path + ".bak");

        var defaults = store.Load();
        Assert.Equal("SettingsLoadFailed", store.StatusKey);
        Assert.NotEqual("ar", defaults.Language);
        store.Save(defaults);

        Assert.Equal("SettingsSaveFailed", store.StatusKey);
        Assert.Equal(future, File.ReadAllText(path));
        Assert.Equal(backup, File.ReadAllText(path + ".bak"));
    }

    /// <summary>Export cannot silently downgrade an unsupported in-memory schema either.</summary>
    [Fact]
    public void FutureSnapshot_CannotBeExportedAsTheCurrentSchema()
    {
        var path = Path.Combine(_directory, "future-export.json");
        var store = new SettingsService(logger, manual, Path.Combine(_directory, "settings.json"));

        Assert.False(ExportWholeBackup(store, new() { SchemaVersion = 999 }, path));
        Assert.Equal("SettingsExportFailed", store.StatusKey);
        Assert.False(File.Exists(path));
    }

    /// <summary>Legacy explicit minute corrections migrate to their selected location only.</summary>
    [Fact]
    public void VersionFourCorrections_MigrateOnlyToTheirOwnLocationAndKeysAreCaseInsensitive()
    {
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, """{"SchemaVersion":4,"LocationId":"SIDON","MinuteAdjustments":{"Maghrib":5,"Sunrise":-2}}""");
        var store = new SettingsService(logger, manual, path);
        var settings = store.Load();

        Assert.Equal(5, settings.SchemaVersion);
        Assert.Single(settings.LocationMinuteAdjustments);
        Assert.Equal(5, settings.LocationMinuteAdjustments["sidon"][Prayer.Maghrib]);
        Assert.Equal(-2, settings.LocationMinuteAdjustments["sidon"][Prayer.Sunrise]);
        settings.LocationId = "BEIRUT";
        settings.MinuteAdjustments = [];
        store.Save(settings);
        var loaded = store.Load();
        Assert.Empty(loaded.MinuteAdjustments);
        Assert.Empty(loaded.LocationMinuteAdjustments["beirut"]);
        Assert.Equal(5, loaded.LocationMinuteAdjustments["Sidon"][Prayer.Maghrib]);
    }

    /// <summary>Imports normalize case-insensitive city identities, correction bounds and ledger capacity.</summary>
    [Fact]
    public void ImportedLocationCorrectionsAndReminderLedger_AreNormalizedAndBounded()
    {
        var path = Path.Combine(_directory, "import.json");
        var json = JsonSerializer.Serialize(new
        {
            SchemaVersion = 5,
            LocationId = "SIDON",
            MinuteAdjustments = new Dictionary<string, int> { ["Fajr"] = 1 },
            PrayerNotifications = new Dictionary<string, object> { ["999"] = new { MinutesBefore = 900 } },
            LocationMinuteAdjustments = new Dictionary<string, Dictionary<string, int>?>
            {
                ["sidon"] = new() { ["Fajr"] = -900, ["Sunrise"] = 900 },
                ["SIDON"] = new() { ["Maghrib"] = 900, ["999"] = 2 },
                ["Other"] = null,
                [""] = new()
            },
            RecentPrayerAlerts = Enumerable.Range(0, 150).Select(index => $"alert-{index}").Concat(["ALERT-149", ""]).ToArray()
        });
        File.WriteAllText(path, json);
        var settings = ReadWholeBackup(new SettingsService(logger, manual, Path.Combine(_directory, "active.json")), path);

        Assert.NotNull(settings);
        Assert.Equal(6, settings.PrayerNotifications.Count);
        Assert.Equal(2, settings.LocationMinuteAdjustments.Count);
        Assert.Equal(60, settings.MinuteAdjustments[Prayer.Maghrib]);
        Assert.Single(settings.MinuteAdjustments);
        Assert.Empty(settings.LocationMinuteAdjustments["OTHER"]);
        Assert.Equal(128, settings.RecentPrayerAlerts.Count);
        Assert.Equal("alert-22", settings.RecentPrayerAlerts[0]);
        Assert.Equal("alert-149", settings.RecentPrayerAlerts[^1]);
    }

    [Fact]
    public void OldStockAdhkarMigrateWithoutChangingCustomEntries()
    {
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, """{"schemaVersion":3,"textAdhkar":{"texts":["سبحان الله","الحمد لله","الله أكبر"]},"adhkar":{"enabled":false,"audioFiles":[]}}""");
        var loaded = new SettingsService(logger, manual, path).Load();
        Assert.Empty(loaded.TextAdhkar.Texts);
        Assert.Equal(BundledAudio.AdhkarFiles, loaded.Adhkar.AudioFiles);
        loaded.TextAdhkar.Texts.Add("Custom");
        new SettingsService(logger, manual, path).Save(loaded);
        var saved = new SettingsService(logger, manual, path).Load();
        Assert.Equal(["Custom"], saved.TextAdhkar.Texts);
    }

    [Fact]
    public void OnlyExistingLocalOggFilesAreAcceptedCaseInsensitively()
    {
        var path = Path.Combine(_directory, "recording.OGG");
        File.WriteAllBytes(path, []);
        Assert.True(AudioPlayer.IsSupportedFile(path));
        var obsolete = Path.Combine(_directory, "recording.mp3");
        File.WriteAllBytes(obsolete, []);
        Assert.False(AudioPlayer.IsSupportedFile(obsolete));
        Assert.False(AudioPlayer.IsSupportedFile("https://example.com/audio.mp3"));
        Assert.False(AudioPlayer.IsSupportedFile(@"\\server\audio.mp3"));
        Assert.False(AudioPlayer.IsSupportedFile(Path.Combine(_directory, "missing.mp3")));
        Assert.False(AudioPlayer.IsSupportedFile("recording.mp3"));
        Assert.False(AudioPlayer.IsSupportedFile(null));
    }

    /// <inheritdoc />
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static bool ExportWholeBackup(SettingsService store, AppSettings settings, string path) =>
        store.ExportBackup(settings, new MovementBreakPreferences(), "All", path);

    private static AppSettings? ReadWholeBackup(SettingsService store, string path) =>
        store.ReadBackup(new AppSettings(), "All", path)?.Settings;

}
