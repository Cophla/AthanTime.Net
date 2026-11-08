using System.Security;
using Microsoft.Win32;

namespace AthanTime.Net.Services;

/// <summary>Registers this app for the current user's login, without admin rights.</summary>
public interface ILoginStartupService
{
    /// <summary>Whether the app's own login entry currently exists.</summary>
    bool IsEnabled();
    /// <summary>Adds or removes only the app's own current-user login entry.</summary>
    void SetEnabled(bool enabled);
}

/// <summary>Uses current-user Run, XDG autostart or LaunchAgent entries pointing to the installed executable.</summary>
public sealed class LoginStartupService : ILoginStartupService
{
    private const string WindowsRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string WindowsValue = "AthanTime";
    private readonly string _executable = Environment.ProcessPath ?? "";
    private readonly string _home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <inheritdoc />
    public bool IsEnabled()
    {
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.OpenSubKey(WindowsRunKey);
            return key?.GetValue(WindowsValue) is string;
        }
        return File.Exists(EntryPath());
    }

    /// <inheritdoc />
    public void SetEnabled(bool enabled)
    {
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.CreateSubKey(WindowsRunKey);
            if (enabled) key.SetValue(WindowsValue, BuildWindowsRunValue(Executable()));
            else key.DeleteValue(WindowsValue, throwOnMissingValue: false);
            return;
        }

        var path = EntryPath();
        if (!enabled) { if (File.Exists(path)) File.Delete(path); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, OperatingSystem.IsMacOS()
            ? BuildMacLaunchAgent(Executable()) : BuildLinuxDesktopEntry(Executable()));
    }

    /// <summary>Formats the Linux autostart entry, quoting the executable without invoking a shell.</summary>
    public static string BuildLinuxDesktopEntry(string executable)
    {
        var escaped = executable.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("$", "\\$", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal)
            .Replace("%", "%%", StringComparison.Ordinal);
        return $"[Desktop Entry]\nType=Application\nName=Athan Time\nExec=\"{escaped}\" --start-minimized\nTerminal=false\n";
    }

    /// <summary>Quotes the Windows executable as one HKCU Run command.</summary>
    public static string BuildWindowsRunValue(string executable) => $"\"{executable}\" --start-minimized";

    /// <summary>Formats a current-user macOS LaunchAgent with no shell interpolation.</summary>
    public static string BuildMacLaunchAgent(string executable) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
        + "<plist version=\"1.0\"><dict><key>Label</key><string>org.athantime.net</string>"
        + "<key>ProgramArguments</key><array><string>" + SecurityElement.Escape(executable)
        + "</string><string>--start-minimized</string></array>"
        + "<key>RunAtLoad</key><true/><key>KeepAlive</key><false/></dict></plist>\n";

    private string Executable()
    {
        if (string.IsNullOrWhiteSpace(_executable) || !Path.IsPathFullyQualified(_executable)
            || _executable.IndexOfAny(['\r', '\n']) >= 0
            || !File.Exists(_executable) || Path.GetFileNameWithoutExtension(_executable)
                .Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Install or publish the app before enabling login startup.");
        return _executable;
    }

    private string EntryPath()
    {
        if (string.IsNullOrWhiteSpace(_home)) throw new InvalidOperationException("User profile is unavailable.");
        if (OperatingSystem.IsMacOS())
            return Path.Combine(_home, "Library", "LaunchAgents", "org.athantime.net.plist");
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(config) || !Path.IsPathFullyQualified(config))
            config = Path.Combine(_home, ".config");
        return Path.Combine(config, "autostart", "athantime.desktop");
    }
}
