using System.Xml.Linq;
using AthanTime.Net.Services;
using Serilog;

namespace AthanTime.Tests;

/// <summary>Exercises log deletion and portable login-entry rendering without touching a real user profile.</summary>
public sealed class LoggingAndStartupTests
{
    [Fact]
    public void DisablingLoggingClosesAndDeletesOnlyAppLogs()
    {
        var root = Directory.CreateTempSubdirectory("athan-logging-test-").FullName;
        try
        {
            using var sink = new FileLogSink(root);
            sink.SetEnabled(true);
            using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
            logger.Information("A test event");
            var logs = Path.Combine(root, "Logs");
            Assert.NotEmpty(Directory.EnumerateFiles(logs, "athan-*.log"));
            sink.SetEnabled(false);
            Assert.False(Directory.Exists(logs));

            Directory.CreateDirectory(logs);
            File.WriteAllText(Path.Combine(logs, "notes.txt"), "user file");
            File.WriteAllText(Path.Combine(logs, "athan-old.log"), "old app log");
            sink.SetEnabled(false);
            Assert.False(File.Exists(Path.Combine(logs, "athan-old.log")));
            Assert.True(File.Exists(Path.Combine(logs, "notes.txt")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void LinuxAndMacLoginEntriesQuotePathsWithoutShells()
    {
        const string path = "/Applications/Athan & Time/AthanTime.Net";
        var desktop = LoginStartupService.BuildLinuxDesktopEntry(path);
        Assert.Contains("Exec=\"/Applications/Athan & Time/AthanTime.Net\" --start-minimized", desktop);
        Assert.DoesNotContain("sh -c", desktop, StringComparison.OrdinalIgnoreCase);
        var plist = XDocument.Parse(LoginStartupService.BuildMacLaunchAgent(path));
        Assert.Contains(path, plist.Descendants("string").Select(item => item.Value));
        Assert.Contains("--start-minimized", plist.Descendants("string").Select(item => item.Value));
        Assert.Equal("\"C:\\Program Files\\Athan Time\\AthanTime.Net.exe\" --start-minimized",
            LoginStartupService.BuildWindowsRunValue(@"C:\Program Files\Athan Time\AthanTime.Net.exe"));
    }
}
