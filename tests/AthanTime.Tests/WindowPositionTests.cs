using Avalonia;
using AthanTime.Net.Services;
using Microsoft.Extensions.Logging;

namespace AthanTime.Tests;

/// <summary>Exercises device-local coordinate persistence and multi-monitor geometry without native desktop changes.</summary>
public sealed class WindowPositionTests(ILogger<WindowPositionService> logger)
{
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
