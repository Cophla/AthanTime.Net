using AthanTime.Net.Models;
using AthanTime.Net.Services;
using AthanTime.Net.ViewModels;
using Microsoft.Extensions.Logging;

namespace AthanTime.Tests;

/// <summary>Constructor-injected application services with deterministic clock/audio/settings boundaries.</summary>
public sealed class ViewModelHarness(IPrayerConventionRepository conventions, ILocationRepository locations,
    ILocalizationService localization, IHijriDateService hijri, ManualLocationService manual,
    ILogger<MainViewModel> logger, DefaultAdhkarRepository defaultAdhkar, LoggingLevelController loggingLevel,
    ILoginStartupService loginStartup, AdhkarScheduler adhkar, TextAdhkarScheduler textAdhkar, TimeZoneService timeZones)
{
    public TestClock Clock { get; } = new();
    public MemorySettings Settings { get; } = new();
    public RecordingAudio Audio { get; } = new();
    public CountingCalculator Calculator { get; } = new();

    /// <summary>Creates the screen without starting the clock; an optional controlled repository exercises search concurrency.</summary>
    public MainViewModel Create(ILocationRepository? locationRepository = null) => new(Calculator, conventions,
        locationRepository ?? locations, localization, hijri, Settings,
        logger, manual, Audio, adhkar, Clock, textAdhkar, defaultAdhkar, loggingLevel,
        loginStartup, timeZones);
}

/// <summary>Deterministic UTC test clock.</summary>
public sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);
    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Mutable in-memory JSON model boundary, without writing the user's actual profile.</summary>
public sealed class MemorySettings : ISettingsService
{
    public AppSettings Value { get; } = new()
    {
        LocationId = "manual-test",
        UseAutomaticConvention = false,
        SavedLocations = [new("manual-test", "Test place", "", 33.56, 35.37, "Etc/UTC")],
        PrayerNotifications = Enum.GetValues<Prayer>().ToDictionary(prayer => prayer, _ => new PrayerNotificationSettings())
    };
    public int Saves { get; private set; }
    /// <inheritdoc />
    public string StatusKey { get; private set; } = "";
    /// <inheritdoc />
    public event EventHandler? StatusChanged;
    /// <summary>Reports a deterministic storage outcome without accessing the filesystem.</summary>
    public void ReportStatus(string key) { StatusKey = key; StatusChanged?.Invoke(this, EventArgs.Empty); }
    /// <inheritdoc />
    public AppSettings Load() => Value;
    /// <inheritdoc />
    public void Save(AppSettings settings) => Saves++;
    /// <inheritdoc />
    public bool Export(AppSettings settings, string path) => true;
    /// <inheritdoc />
    public AppSettings? ReadImport(string path) => null;
}

/// <summary>Records playback requests without opening a sound device.</summary>
public sealed class RecordingAudio : IAudioPlayer
{
    public bool IsBusy { get; set; }
    public List<string> Played { get; } = [];
    public event EventHandler<string>? StatusChanged;
    /// <inheritdoc />
    public Task PlayAsync(string path, int volume, bool interrupt = true)
    {
        if (!interrupt && IsBusy) return Task.CompletedTask;
        Played.Add(path); IsBusy = true;
        StatusChanged?.Invoke(this, "AudioPlaying");
        return Task.CompletedTask;
    }
    /// <inheritdoc />
    public Task StopAsync() { IsBusy = false; return Task.CompletedTask; }
}

/// <summary>Counts calculation calls and supplies simple located dates to exercise scheduling, not astronomy.</summary>
public sealed class CountingCalculator : IPrayerCalculator
{
    public int Calls { get; private set; }
    public TimeOnly Fajr { get; set; } = new(5, 0);
    public TimeOnly Isha { get; set; } = new(20, 0);
    /// <inheritdoc />
    public DailyPrayerTimes Calculate(DateOnly date, LocationInfo location, CalculationMethod method,
        AsrJuristic asr, HighLatitudeRule highLatitudeRule, IReadOnlyDictionary<Prayer, int>? adjustments = null,
        SummerTimeSettings? summerTime = null)
    {
        Calls++;
        TimeOnly[] times = [Fajr, new(6, 0), new(12, 0), new(15, 0), new(18, 0), Isha];
        return new(date, Enum.GetValues<Prayer>().Select((prayer, index) => new PrayerTime(prayer,
            date.ToDateTime(times[index]).AddMinutes(adjustments?.GetValueOrDefault(prayer) ?? 0))).ToArray(),
            date.ToDateTime(new TimeOnly(23, 30)), 160);
    }
}

/// <summary>In-memory login registration so tests never modify an OS profile.</summary>
public sealed class FakeLoginStartupService : ILoginStartupService
{
    public bool Enabled { get; private set; }
    /// <inheritdoc />
    public bool IsEnabled() => Enabled;
    /// <inheritdoc />
    public void SetEnabled(bool enabled) => Enabled = enabled;
}
