using System.IO.Compression;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace AthanTime.Net.Services;

/// <summary>Controls the optional file log and diagnostic verbosity without restarting.</summary>
public sealed class LoggingLevelController(LoggingLevelSwitch levelSwitch, FileLogSink fileSink,
    ILogger<LoggingLevelController> logger)
{
    /// <summary>Enables logging or closes the sink and deletes only Athan Time log files.</summary>
    public void SetEnabled(bool enabled)
    {
        if (!enabled && fileSink.Enabled) logger.LogInformation("File logging disabled by the user");
        fileSink.SetEnabled(enabled);
        if (enabled) logger.LogInformation("File logging enabled");
    }

    /// <summary>Applies the JSON-backed per-user debug preference.</summary>
    public void SetDebug(bool enabled)
    {
        levelSwitch.MinimumLevel = enabled ? LogEventLevel.Debug : LogEventLevel.Information;
        logger.LogInformation("Diagnostic logging {State}", enabled ? "enabled" : "disabled");
    }
}

/// <summary>Opens the app's file logger only while logging is enabled.</summary>
public sealed class FileLogSink : ILogEventSink, IDisposable
{
    private readonly object _gate = new();
    private readonly string _directory;
    private Serilog.ILogger? _fileLogger;
    private volatile bool _enabled;

    /// <summary>Uses only the application's per-user Logs directory.</summary>
    public FileLogSink(string appDataDirectory) => _directory = Path.Combine(Path.GetFullPath(appDataDirectory), "Logs");

    /// <summary>Whether any app logging is currently allowed.</summary>
    public bool Enabled => _enabled;

    /// <summary>Creates or closes the file sink, deleting only app-owned log files when disabled.</summary>
    public void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (enabled == _enabled)
            {
                if (!enabled) DeleteLogs();
                return;
            }
            if (enabled)
            {
                Directory.CreateDirectory(_directory);
                _fileLogger = new LoggerConfiguration().MinimumLevel.Verbose()
                    .WriteTo.File(Path.Combine(_directory, "athan-.log"), rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 14, fileSizeLimitBytes: 5 * 1024 * 1024,
                        rollOnFileSizeLimit: true, shared: true).CreateLogger();
                _enabled = true;
            }
            else
            {
                _enabled = false;
                (_fileLogger as IDisposable)?.Dispose();
                _fileLogger = null;
                DeleteLogs();
            }
        }
    }

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        lock (_gate) _fileLogger?.Write(logEvent);
    }

    /// <summary>Streams app log files into a user-selected ZIP; never includes settings or other files.</summary>
    public bool Export(Stream destination)
    {
        lock (_gate)
        {
            if (!_enabled || !Directory.Exists(_directory)) return false;
            var files = Directory.GetFiles(_directory, "athan-*.log");
            if (files.Length == 0) return false;
            using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
            foreach (var path in files)
            {
                using var source = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var entry = archive.CreateEntry(Path.GetFileName(path), CompressionLevel.Fastest).Open();
                source.CopyTo(entry);
            }
            return true;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _enabled = false;
            (_fileLogger as IDisposable)?.Dispose();
            _fileLogger = null;
        }
    }

    private void DeleteLogs()
    {
        DeleteOwnLogs(_directory);
        // Remove old lowercase output from earlier versions on case-sensitive systems.
        var legacy = Path.Combine(Path.GetDirectoryName(_directory)!, "logs");
        if (!legacy.Equals(_directory, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) DeleteOwnLogs(legacy);
    }

    private static void DeleteOwnLogs(string directory)
    {
        if (!Directory.Exists(directory) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return;
        var parent = Path.GetDirectoryName(directory)!;
        if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0) return;
        foreach (var file in Directory.EnumerateFiles(directory, "athan-*.log", SearchOption.TopDirectoryOnly))
            File.Delete(file);
        if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
    }
}
