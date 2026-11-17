using Avalonia;
using AthanTime.Net.Services;
using Microsoft.Extensions.Logging;

namespace AthanTime.Tests;

/// <summary>Exercises device-local coordinate persistence and multi-monitor geometry without native desktop changes.</summary>
public sealed class WindowPositionTests(ILogger<WindowPositionService> logger)
{
    /// <summary>The reported 150% monitor has only 672 logical work-area pixels before decorations.</summary>
    [Theory]
    [InlineData(0, 80)]
    [InlineData(-1920, -1800)]
    public void HighDpiWindowShrinksItsClientHeightAndKeepsItsWholeFrameVisible(int monitorX, int requestedX)
    {
        var area = new PixelRect(monitorX, 0, 1920, 1008);
        var result = WindowPositionService.Fit(new(requestedX, 151), new(1180, 800), new(980, 600),
            new(16, 39), 1.5, [area]);
        Assert.NotNull(result);
        var fit = result.Value;
        Assert.Equal(new Size(1180, 633), fit.ClientSize);
        Assert.Equal(new Size(980, 600), fit.MinimumSize);
        Assert.Equal(new PixelPoint(requestedX, 0), fit.Position);
        var frame = PixelSize.FromSize(new(fit.ClientSize.Width + 16, fit.ClientSize.Height + 39), 1.5);
        Assert.True(area.Contains(new PixelRect(fit.Position, frame)));
        var repeated = WindowPositionService.Fit(fit.Position, fit.ClientSize, new(980, 600), new(16, 39), 1.5, [area]);
        Assert.Equal(result, repeated);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    [InlineData(2.0)]
    public void OversizedFramesFitBothAxesWithFractionalDesktopScaling(double scale)
    {
        var area = new PixelRect(-1920, 24, 1920, 984);
        var result = WindowPositionService.Fit(new(-10, 980), new(2400, 1600), new(980, 600), new(14, 38), scale, [area]);
        Assert.NotNull(result);
        var fit = result.Value;
        Assert.True(fit.ClientSize.Width <= 1920 / scale - 14);
        Assert.True(fit.ClientSize.Height <= 984 / scale - 38);
        var frame = PixelSize.FromSize(new(fit.ClientSize.Width + 14, fit.ClientSize.Height + 38), scale);
        Assert.True(area.Contains(new PixelRect(fit.Position, frame)));
    }

    [Fact]
    public void SmallScreensTemporarilyLowerMinimumsAndLargerScreensRestoreThem()
    {
        var minimum = new Size(980, 600);
        var small = WindowPositionService.Fit(new(50, 50), new(1180, 800), minimum, new(16, 39), 2,
            [new PixelRect(0, 0, 1920, 1008)])!.Value;
        Assert.Equal(new Size(944, 465), small.MinimumSize);
        Assert.Equal(small.MinimumSize, small.ClientSize);
        var larger = WindowPositionService.Fit(small.Position, small.ClientSize, minimum, new(16, 39), 1,
            [new PixelRect(0, 0, 1920, 1032)])!.Value;
        Assert.Equal(minimum, larger.MinimumSize);
        Assert.Equal(minimum, larger.ClientSize);
    }

    [Fact]
    public void MissingMonitorsCenterTheFittedFrameOnThePrimaryWorkArea()
    {
        var area = new PixelRect(0, 40, 1280, 632);
        var fit = WindowPositionService.Fit(new(int.MaxValue, int.MinValue), new(1180, 800), new(980, 600),
            new(16, 39), 1, [area])!.Value;
        Assert.Equal(new Size(1180, 593), fit.ClientSize);
        Assert.Equal(new PixelPoint(42, 40), fit.Position);
        Assert.True(area.Contains(new PixelRect(fit.Position, new PixelSize(1196, 632))));
    }

    [Fact]
    public void AlreadyVisibleWindowsAndDesktopPointCoordinatesAreUnchanged()
    {
        // A Retina backend can render at 2x while desktop coordinates and DesktopScaling remain 1x.
        var fit = WindowPositionService.Fit(new(60, 70), new(1180, 800), new(980, 600), new(0, 28), 1,
            [new PixelRect(0, 24, 1920, 1032)])!.Value;
        Assert.Equal(new PixelPoint(60, 70), fit.Position);
        Assert.Equal(new Size(1180, 800), fit.ClientSize);
        Assert.Equal(new Size(980, 600), fit.MinimumSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidDesktopScalingDefersWithoutChangingGeometry(double scale) =>
        Assert.Null(WindowPositionService.Fit(new(10, 10), new(1180, 800), new(980, 600), new(0, 30), scale,
            [new PixelRect(0, 0, 1920, 1080)]));

    [Fact]
    public void MissingWorkAreasOrInvalidClientAndFrameSizesDeferToTheWindowManager()
    {
        Assert.Null(WindowPositionService.Fit(new(), new(1180, 800), new(980, 600), new(), 1, []));
        Assert.Null(WindowPositionService.Fit(new(), new(double.NaN, 800), new(980, 600), new(), 1, [new(0, 0, 1920, 1080)]));
        Assert.Null(WindowPositionService.Fit(new(), new(1180, 800), new(980, 600), new(0, double.NaN), 1, [new(0, 0, 1920, 1080)]));
        Assert.Null(WindowPositionService.Fit(new(), new(1180, 800), new(980, 600), new(0, 100), 1, [new(0, 0, 100, 50)]));
    }

    [Theory]
    [InlineData(60, 90, 1180, 800, 60, 90)]
    [InlineData(1800, 1000, 1180, 800, 740, 280)]
    [InlineData(2600, 200, 1180, 800, 370, 160)]
    [InlineData(int.MaxValue, int.MinValue, 1180, 800, 370, 160)]
    [InlineData(-32000, -32000, 1180, 800, 370, 160)]
    [InlineData(100, 100, 2000, 1400, 0, 40)]
    public void PositionFitsTheAvailableWorkArea(int x, int y, int width, int height, int expectedX, int expectedY)
    {
        Assert.Equal(new PixelPoint(expectedX, expectedY), WindowPositionService.Constrain(
            new PixelPoint(x, y), new PixelSize(width, height), [new PixelRect(0, 40, 1920, 1040)]));
    }

    [Fact]
    public void NegativeMonitorCoordinatesAndDisplayGapsAreHandled()
    {
        PixelRect[] areas = [new(0, 40, 1920, 1040), new(-1920, 0, 1920, 1080), new(2560, 0, 3840, 2160)];
        Assert.Equal(new PixelPoint(-1600, 100), WindowPositionService.Constrain(new(-1600, 100), new(1180, 800), areas));
        Assert.Equal(new PixelPoint(-1180, 100), WindowPositionService.Constrain(new(-100, 100), new(1180, 800), areas));
        Assert.Equal(new PixelPoint(370, 160), WindowPositionService.Constrain(new(2200, 100), new(1180, 800), areas));
        // The physical frame already accounts for 150% desktop scaling.
        Assert.Equal(new PixelPoint(4630, 960), WindowPositionService.Constrain(new(6200, 2000), new(1770, 1200), areas));
    }

    [Fact]
    public void MissingOrInvalidDisplayInformationDefersToTheWindowManager()
    {
        Assert.Null(WindowPositionService.Constrain(new(10, 10), new(1180, 800), []));
        Assert.Null(WindowPositionService.Constrain(new(10, 10), new(1180, 800), [new PixelRect()]));
        Assert.Null(WindowPositionService.Constrain(new(10, 10), new(0, 800), [new(0, 0, 1920, 1080)]));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"X\":30}")]
    [InlineData("{\"Y\":40}")]
    [InlineData("{\"X\":2147483648,\"Y\":40}")]
    [InlineData("{\"X\":\"bad\",\"Y\":40}")]
    public void DamagedCoordinatesStayUnchangedUntilALaterSuccessfulSave(string json)
    {
        var directory = Directory.CreateTempSubdirectory("athan-position-test-");
        try
        {
            var path = Path.Combine(directory.FullName, "window-position.json");
            File.WriteAllText(path, json);
            var store = new WindowPositionService(logger, path);
            Assert.Null(store.Load());
            Assert.Equal(json, File.ReadAllText(path));
            Assert.True(store.Save(new(-1500, 60)));
            Assert.Equal(new WindowPosition(-1500, 60), new WindowPositionService(logger, path).Load());
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void MissingProfileDoesNotWriteAndFailedSavePreservesTheLastPosition()
    {
        var directory = Directory.CreateTempSubdirectory("athan-position-test-");
        try
        {
            var path = Path.Combine(directory.FullName, "new-profile", "window-position.json");
            var store = new WindowPositionService(logger, path);
            Assert.Null(store.Load());
            Assert.False(Directory.Exists(Path.GetDirectoryName(path)));
            Assert.True(store.Save(new(80, 90)));
            var bytes = File.ReadAllBytes(path);
            Directory.CreateDirectory(path + ".tmp");
            Assert.False(store.Save(new(120, 140)));
            Assert.Equal(bytes, File.ReadAllBytes(path));
            Directory.Delete(path + ".tmp");
            Assert.True(store.Save(new(120, 140)));
            Assert.Equal(new WindowPosition(120, 140), store.Load());
        }
        finally { directory.Delete(recursive: true); }
    }
}
