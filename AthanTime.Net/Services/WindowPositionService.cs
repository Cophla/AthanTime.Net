using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Microsoft.Extensions.Logging;

namespace AthanTime.Net.Services;

/// <summary>Device-local desktop coordinates, separate from transferable application preferences.</summary>
public sealed record WindowPosition([property: JsonRequired] int X, [property: JsonRequired] int Y);

/// <summary>Persists normal coordinates and fits desktop windows to the available work area.</summary>
public sealed class WindowPositionService(ILogger<WindowPositionService> logger, string path)
{
    private readonly ILogger<WindowPositionService> _logger = logger;
    private readonly string _path = Path.GetFullPath(path);

    /// <summary>Reads optional coordinates without writing; a missing or damaged file keeps centered startup.</summary>
    public WindowPosition? Load()
    {
        try
        {
            if (new FileInfo(_path).Attributes == (FileAttributes)(-1)) return null;
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
        if (FindWorkArea(requested, workAreas) is not { } area) return null;
        if (!area.Contains(requested))
        {
            requested = area.CenterRect(new PixelRect(frame)).Position;
        }
        // Keep the title bar reachable even if the window's minimum size exceeds the display.
        return new PixelPoint(Math.Clamp(requested.X, area.X, Math.Max(area.X, area.Right - frame.Width)),
            Math.Clamp(requested.Y, area.Y, Math.Max(area.Y, area.Bottom - frame.Height)));
    }

    /// <summary>Fits logical client/minimum sizes and physical position, reserving native frame decorations.</summary>
    public static (PixelPoint Position, Size ClientSize, Size MinimumSize)? Fit(
        PixelPoint requested, Size clientSize, Size minimumSize, Size frameBorder,
        double desktopScaling, IReadOnlyList<PixelRect> workAreas)
    {
        if (!double.IsFinite(desktopScaling) || desktopScaling <= 0
            || !double.IsFinite(clientSize.Width) || !double.IsFinite(clientSize.Height)
            || clientSize.Width <= 0 || clientSize.Height <= 0
            || !double.IsFinite(minimumSize.Width) || !double.IsFinite(minimumSize.Height)
            || minimumSize.Width < 0 || minimumSize.Height < 0
            || !double.IsFinite(frameBorder.Width) || !double.IsFinite(frameBorder.Height)
            || frameBorder.Width < 0 || frameBorder.Height < 0
            || FindWorkArea(requested, workAreas) is not { } area) return null;

        // DesktopScaling, rather than rendering density, also handles macOS desktop coordinates.
        var maximum = new Size(Math.Floor(area.Width / desktopScaling - frameBorder.Width),
            Math.Floor(area.Height / desktopScaling - frameBorder.Height));
        if (maximum.Width < 1 || maximum.Height < 1) return null;
        var minimum = new Size(Math.Min(minimumSize.Width, maximum.Width), Math.Min(minimumSize.Height, maximum.Height));
        var client = new Size(Math.Clamp(clientSize.Width, minimum.Width, maximum.Width),
            Math.Clamp(clientSize.Height, minimum.Height, maximum.Height));
        var frame = PixelSize.FromSize(new Size(client.Width + frameBorder.Width, client.Height + frameBorder.Height), desktopScaling);
        return (Constrain(requested, frame, [area])!.Value, client, minimum);
    }

    private static PixelRect? FindWorkArea(PixelPoint requested, IReadOnlyList<PixelRect> workAreas)
    {
        PixelRect? fallback = null;
        foreach (var area in workAreas)
        {
            if (area.Width <= 0 || area.Height <= 0) continue;
            fallback ??= area;
            if (area.Contains(requested)) return area;
        }
        return fallback;
    }
}
