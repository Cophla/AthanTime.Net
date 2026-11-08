using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Microsoft.Extensions.Logging;

namespace AthanTime.Net.Services;

/// <summary>Device-local desktop coordinates, separate from transferable application preferences.</summary>
public sealed record WindowPosition([property: JsonRequired] int X, [property: JsonRequired] int Y);

/// <summary>Persists the normal main-window position and keeps restored coordinates on an available display.</summary>
public sealed class WindowPositionService(ILogger<WindowPositionService> logger, string path)
{
    private readonly ILogger<WindowPositionService> _logger = logger;
    private readonly string _path = Path.GetFullPath(path);

    /// <summary>Reads optional coordinates without writing; a missing or damaged file keeps centered startup.</summary>
    public WindowPosition? Load()
    {
        try
        {
            return JsonSerializer.Deserialize<WindowPosition>(File.ReadAllText(_path))
                ?? throw new JsonException("Window coordinates must be an object.");
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException) { return null; }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Window position could not be loaded; centered startup is used");
            return null;
        }
    }

    /// <summary>Atomically saves coordinates; failures leave the window usable and can be retried later.</summary>
    public bool Save(WindowPosition position)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(position));
            File.Move(_path + ".tmp", _path, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Window position could not be saved");
            return false;
        }
        finally
        {
            try { File.Delete(_path + ".tmp"); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { _logger.LogWarning(exception, "Temporary window-position file could not be removed"); }
        }
    }

    /// <summary>Fits a physical window frame to a work area; absent monitors fall back to the first (primary) area.</summary>
    public static PixelPoint? Constrain(PixelPoint requested, PixelSize frame, IReadOnlyList<PixelRect> workAreas)
    {
        if (frame.Width <= 0 || frame.Height <= 0) return null;
        var available = workAreas.Where(area => area.Width > 0 && area.Height > 0).ToArray();
        if (available.Length == 0) return null;
        var area = available.FirstOrDefault(candidate => candidate.Contains(requested));
        if (area.Width == 0)
        {
            area = available[0];
            requested = area.CenterRect(new PixelRect(frame)).Position;
        }
        // Keep the title bar reachable even if the window's minimum size exceeds the display.
        return new PixelPoint(Math.Clamp(requested.X, area.X, Math.Max(area.X, area.Right - frame.Width)),
            Math.Clamp(requested.Y, area.Y, Math.Max(area.Y, area.Bottom - frame.Height)));
    }
}
