using Microsoft.Extensions.Logging;

namespace AthanTime.Net.Services;

/// <summary>Stable cleanup exit codes shared by the app, uninstall UI and user-run helpers.</summary>
public enum UserProfileCleanupResult
{
    /// <summary>Known current-user files were removed or were already absent.</summary>
    Success = 0,
    /// <summary>The OS did not provide an absolute user-profile path.</summary>
    InvalidProfilePath = 3,
    /// <summary>Cleanup encountered an I/O or startup-registration failure.</summary>
    Failed = 4,
    /// <summary>A linked profile/log path was refused before any deletion.</summary>
    LinkedPath = 5,
    /// <summary>Profile files or login registration could not be accessed.</summary>
    AccessDenied = 6
}

/// <summary>Removes only app-owned current-user preferences after explicit uninstall/helper consent.</summary>
public sealed class UserProfileCleanup(string directory, ILoginStartupService loginStartup, ILogger<UserProfileCleanup> logger)
{
    /// <summary>Disables current-user login startup and deletes known profile files; recordings and other users are untouched.</summary>
    public UserProfileCleanupResult Remove()
    {
        try
        {
            if (!Path.IsPathFullyQualified(directory)) return UserProfileCleanupResult.InvalidProfilePath;
            var root = Path.GetFullPath(directory);
            var logs = Path.Combine(root, "Logs");
            if (IsLink(root) || IsLink(logs)) return RefuseLink();
            var files = new[] { "settings.json", "settings.json.bak", "settings.json.tmp", "settings.json.bak.tmp",
                "window-position.json", "window-position.json.tmp" }.Select(name => Path.Combine(root, name)).ToList();
            if (Directory.Exists(logs)) files.AddRange(Directory.EnumerateFiles(logs, "athan-*.log", SearchOption.TopDirectoryOnly));
            // Validate before deleting anything; never traverse linked profile/log locations.
            if (files.Any(IsLink)) return RefuseLink();
            if (files.Any(Directory.Exists)) throw new IOException("An owned profile file path is a directory.");
            loginStartup.SetEnabled(false);
            foreach (var file in files)
                if (Path.Exists(file)) File.Delete(file);
            RemoveIfEmpty(logs);
            RemoveIfEmpty(root);
            logger.LogInformation("Removed app-owned current-user settings; unrelated files retained");
            return UserProfileCleanupResult.Success;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException
            or InvalidOperationException)
        {
            logger.LogWarning(exception, "Current-user settings cleanup failed; retry after closing the app and checking access");
            return exception is UnauthorizedAccessException or System.Security.SecurityException
                ? UserProfileCleanupResult.AccessDenied : UserProfileCleanupResult.Failed;
        }
    }

    private UserProfileCleanupResult RefuseLink()
    {
        logger.LogWarning("Profile cleanup refused a linked file or directory before deletion");
        return UserProfileCleanupResult.LinkedPath;
    }

    private static bool IsLink(string path) =>
        Path.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static void RemoveIfEmpty(string path)
    {
        if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path);
    }
}
