using AthanTime.Net.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AthanTime.Tests;

/// <summary>Exercises explicit cleanup only in temporary profiles, with fake login integration.</summary>
public sealed class UserProfileCleanupTests
{
    [Theory]
    [InlineData(true, UserProfileCleanupResult.AccessDenied)]
    [InlineData(false, UserProfileCleanupResult.Failed)]
    public void StartupFailureReportsItsCauseAndPreservesProfile(bool denied, UserProfileCleanupResult expected)
    {
        var directory = Directory.CreateTempSubdirectory("athan-cleanup-startup-");
        try
        {
            var path = Path.Combine(directory.FullName, "settings.json");
            File.WriteAllText(path, "keep");
            var startup = new FailingStartup(denied);
            Assert.Equal(expected, new UserProfileCleanup(directory.FullName, startup, NullLogger<UserProfileCleanup>.Instance).Remove());
            Assert.Equal("keep", File.ReadAllText(path));
        }
        finally { directory.Delete(true); }
    }

    private sealed class FailingStartup(bool denied) : ILoginStartupService
    {
        public bool IsEnabled() => true;
        public void SetEnabled(bool enabled)
        {
            if (denied) throw new UnauthorizedAccessException("Test denial");
            throw new IOException("Test I/O failure");
        }
    }

    [Fact]
    public void RelativePathsAreRejectedWithoutChangingLoginStartup()
    {
        var startup = new FakeLoginStartupService();
        startup.SetEnabled(true);
        Assert.Equal(UserProfileCleanupResult.InvalidProfilePath, new UserProfileCleanup("relative-profile", startup, NullLogger<UserProfileCleanup>.Instance).Remove());
        Assert.True(startup.IsEnabled());
    }

    [Fact]
    public void RemovesOwnedFilesButKeepsRecordingsOtherLogsAndOtherUsers()
    {
        var directory = Directory.CreateTempSubdirectory("athan-cleanup-");
        try
        {
            var profile = Directory.CreateDirectory(Path.Combine(directory.FullName, "user-a"));
            var other = Path.Combine(directory.FullName, "user-b-settings.json");
            File.WriteAllText(other, "keep");
            foreach (var name in new[] { "settings.json", "settings.json.bak", "settings.json.tmp", "settings.json.bak.tmp", "window-position.json", "window-position.json.tmp" })
                File.WriteAllText(Path.Combine(profile.FullName, name), "owned");
            Directory.CreateDirectory(Path.Combine(profile.FullName, "Logs"));
            File.WriteAllText(Path.Combine(profile.FullName, "Logs", "athan-20261115.log"), "owned");
            File.WriteAllText(Path.Combine(profile.FullName, "Logs", "other.log"), "keep");
            File.WriteAllText(Path.Combine(profile.FullName, "recording.ogg"), "keep");
            var startup = new FakeLoginStartupService();
            startup.SetEnabled(true);
            var cleanup = new UserProfileCleanup(profile.FullName, startup, NullLogger<UserProfileCleanup>.Instance);
            Assert.Equal(UserProfileCleanupResult.Success, cleanup.Remove());
            Assert.False(startup.IsEnabled());
            Assert.False(File.Exists(Path.Combine(profile.FullName, "settings.json")));
            Assert.False(File.Exists(Path.Combine(profile.FullName, "Logs", "athan-20261115.log")));
            Assert.Equal("keep", File.ReadAllText(other));
            Assert.Equal("keep", File.ReadAllText(Path.Combine(profile.FullName, "recording.ogg")));
            Assert.Equal("keep", File.ReadAllText(Path.Combine(profile.FullName, "Logs", "other.log")));
            Assert.Equal(UserProfileCleanupResult.Success, cleanup.Remove());
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public void MissingOrEmptyProfileDoesNotGetCreatedAndFailureIsReported()
    {
        var directory = Directory.CreateTempSubdirectory("athan-cleanup-empty-");
        try
        {
            var profile = Path.Combine(directory.FullName, "profile");
            var startup = new FakeLoginStartupService();
            var cleanup = new UserProfileCleanup(profile, startup, NullLogger<UserProfileCleanup>.Instance);
            Assert.Equal(UserProfileCleanupResult.Success, cleanup.Remove());
            Assert.False(Directory.Exists(profile));
            Directory.CreateDirectory(profile);
            // A directory in place of an owned file must fail without recursive deletion.
            Directory.CreateDirectory(Path.Combine(profile, "settings.json"));
            File.WriteAllText(Path.Combine(profile, "settings.json", "keep.ogg"), "keep");
            Assert.Equal(UserProfileCleanupResult.Failed, cleanup.Remove());
            Assert.True(File.Exists(Path.Combine(profile, "settings.json", "keep.ogg")));
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public void LinkedLogDirectoryIsRejectedBeforeAnyProfileDeletion()
    {
        var directory = Directory.CreateTempSubdirectory("athan-cleanup-link-");
        try
        {
            var profile = Directory.CreateDirectory(Path.Combine(directory.FullName, "profile"));
            var target = Directory.CreateDirectory(Path.Combine(directory.FullName, "external"));
            File.WriteAllText(Path.Combine(profile.FullName, "settings.json"), "keep");
            File.WriteAllText(Path.Combine(target.FullName, "athan-other.log"), "keep");
            // Windows developer mode/admin rights control symlink creation. A junction fixture
            // is deliberately avoided; lack of rights is a native capability boundary.
            try { Directory.CreateSymbolicLink(Path.Combine(profile.FullName, "Logs"), target.FullName); }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            { Assert.Skip("Symbolic-link creation requires native permissions."); return; }
            var cleanup = new UserProfileCleanup(profile.FullName, new FakeLoginStartupService(), NullLogger<UserProfileCleanup>.Instance);
            Assert.Equal(UserProfileCleanupResult.LinkedPath, cleanup.Remove());
            Assert.Equal("keep", File.ReadAllText(Path.Combine(profile.FullName, "settings.json")));
            Assert.Equal("keep", File.ReadAllText(Path.Combine(target.FullName, "athan-other.log")));
            Directory.Delete(Path.Combine(profile.FullName, "Logs"));
        }
        finally { directory.Delete(true); }
    }
}
