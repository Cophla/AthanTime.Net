namespace AthanTime.Net.Models;

/// <summary>JSON preferences for calculations, per-location corrections and independent prayer/sunrise reminders.</summary>
public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 5;
    public string LocationId { get; set; } = "geonames-276781";
    public string Language { get; set; } = "en";
    public bool UseAutomaticConvention { get; set; } = true;
    public string MethodId { get; set; } = "MWL";
    public AsrJuristic AsrJuristic { get; set; } = AsrJuristic.Standard;
    public HighLatitudeRule HighLatitudeRule { get; set; } = HighLatitudeRule.AngleBased;
    public bool Use24HourClock { get; set; }
    public bool UseDarkMode { get; set; }
    public bool StartMinimized { get; set; }
    public bool StartOnLogin { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool NotificationsEnabled { get; set; } = true;
    public bool PrayerAudioEnabled { get; set; } = true;
    public DateTimeOffset? NotificationsPausedUntilUtc { get; set; }
    public int NotificationPauseMinutes { get; set; } = 60;
    public bool DebugLoggingEnabled { get; set; }
    public bool LoggingEnabled { get; set; } = true;
    /// <summary>Per-event reminder choices; the persisted Sunrise key identifies a time marker, not a prayer.</summary>
    public Dictionary<Prayer, PrayerNotificationSettings> PrayerNotifications { get; set; } = [];
    public int HijriAdjustment { get; set; }
    public Dictionary<Prayer, int> MinuteAdjustments { get; set; } = [];
    /// <summary>Explicit user corrections per location; MinuteAdjustments is the active location's copy.</summary>
    public Dictionary<string, Dictionary<Prayer, int>> LocationMinuteAdjustments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Bounded UTC reminder identities prevent duplicate notifications after restart or clock reversal.</summary>
    public List<string> RecentPrayerAlerts { get; set; } = [];
    public List<LocationInfo> SavedLocations { get; set; } = [];
    public int AudioVolume { get; set; } = 80;
    public AdhkarSettings Adhkar { get; set; } = new();
    public TextAdhkarSettings TextAdhkar { get; set; } = new();
    /// <summary>Explicit daylight-saving overrides by IANA zone; missing entries use automatic rules.</summary>
    public Dictionary<string, SummerTimeSettings> SummerTimeOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Automatic IANA rules, or an explicit user-selected seasonal clock adjustment.</summary>
public sealed class SummerTimeSettings
{
    public bool Automatic { get; set; } = true;
    public bool Enabled { get; set; }
    public int AdjustmentMinutes { get; set; } = 60;
    /// <summary>When automatic is off, follow the user's annual dates instead of forcing a constant state.</summary>
    public bool UseCustomSchedule { get; set; }
    public SummerTimeRule Start { get; set; } = new() { Month = 3, Hour = 2 };
    public SummerTimeRule End { get; set; } = new() { Month = 10, Hour = 3 };
}

/// <summary>A yearly Gregorian calendar day or first/second/third/fourth/last weekday, at local time before the change.</summary>
public sealed class SummerTimeRule
{
    public int Month { get; set; } = 3;
    public bool UseWeekday { get; set; } = true;
    public int Day { get; set; } = 1;
    /// <summary>1–4 selects the nth weekday; 5 selects the last, even in a four-occurrence month.</summary>
    public int Occurrence { get; set; } = 5;
    public DayOfWeek Weekday { get; set; } = DayOfWeek.Sunday;
    public int Hour { get; set; } = 2;
    public int Minute { get; set; }
}

/// <summary>Independent before/at/after and audio choices for one prayer or optional sunrise reminder.</summary>
public sealed class PrayerNotificationSettings
{
    public bool NotifyBefore { get; set; } = true;
    public int MinutesBefore { get; set; } = 15;
    public bool NotifyAtTime { get; set; } = true;
    public bool NotifyAfter { get; set; }
    public int MinutesAfter { get; set; } = 5;
    public bool AudioEnabled { get; set; }
    public string AudioPath { get; set; } = "";
}

/// <summary>Plays one local recording per interval, in list order or randomly.</summary>
public sealed class AdhkarSettings
{
    public bool Enabled { get; set; }
    public bool RandomOrder { get; set; }
    public int IntervalMinutes { get; set; } = 30;
    public List<string> AudioFiles { get; set; } = ["bundled:takbeer", "bundled:alhamdulillah"];
}

/// <summary>Shows one text, closes it after the reading duration, then waits before the next text.</summary>
public sealed class TextAdhkarSettings
{
    public bool Enabled { get; set; }
    public int DisplaySeconds { get; set; } = 20;
    public int PauseMinutes { get; set; } = 30;
    public List<string> Texts { get; set; } = ["سبحان الله", "الحمد لله", "الله أكبر"];
}
