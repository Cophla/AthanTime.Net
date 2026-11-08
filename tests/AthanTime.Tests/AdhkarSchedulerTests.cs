using AthanTime.Net.Models;
using AthanTime.Net.Services;

namespace AthanTime.Tests;

/// <summary>Checks interval semantics, no overlap and no catch-up storms after suspension.</summary>
public sealed class AdhkarSchedulerTests(AdhkarScheduler timer)
{
    private static readonly DateTimeOffset _start = new(2026, 9, 26, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SequentialPlaylist_PlaysOnePerIntervalAndWraps()
    {
        var settings = new AdhkarSettings { Enabled = true, IntervalMinutes = 1, AudioFiles = ["a", "b"] };
        Assert.Null(timer.TakeDue(settings, _start, false));
        Assert.Null(timer.TakeDue(settings, _start.AddSeconds(59), false));
        Assert.Equal("a", timer.TakeDue(settings, _start.AddMinutes(1), false));
        Assert.Null(timer.TakeDue(settings, _start.AddMinutes(1), false));
        Assert.Equal("b", timer.TakeDue(settings, _start.AddMinutes(2), false));
        Assert.Equal("a", timer.TakeDue(settings, _start.AddMinutes(3), false));
    }

    [Fact]
    public void BusyIntervalsAreSkippedAndResumeDoesNotQueue()
    {
        var settings = new AdhkarSettings { Enabled = true, IntervalMinutes = 1, AudioFiles = ["a", "b"] };
        timer.TakeDue(settings, _start, false);
        Assert.Null(timer.TakeDue(settings, _start.AddMinutes(1), true));
        Assert.Null(timer.TakeDue(settings, _start.AddMinutes(1).AddSeconds(1), false));
        Assert.Equal("a", timer.TakeDue(settings, _start.AddDays(1), false));
        Assert.Null(timer.TakeDue(settings, _start.AddDays(1).AddSeconds(1), false));
    }

    [Fact]
    public void RandomModeOnlySelectsPlaylistEntriesAndResetRestartsWait()
    {
        var settings = new AdhkarSettings { Enabled = true, RandomOrder = true, IntervalMinutes = 1, AudioFiles = ["a", "b", "c"] };
        timer.TakeDue(settings, _start, false);
        for (var minute = 1; minute <= 100; minute++)
            Assert.Contains(timer.TakeDue(settings, _start.AddMinutes(minute), false), settings.AudioFiles);
        timer.Reset();
        Assert.Null(timer.TakeDue(settings, _start.AddDays(1), false));
        settings.Enabled = false;
        Assert.Null(timer.TakeDue(settings, _start.AddDays(2), false));
        settings.Enabled = true;
        settings.AudioFiles.Clear();
        Assert.Null(timer.TakeDue(settings, _start.AddDays(3), false));
    }
}
