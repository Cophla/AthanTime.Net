using System.Diagnostics;
using System.IO.Compression;
using System.Xml.Linq;
using InstallerBuilder;

namespace AthanTime.Tests;

/// <summary>Checks archive layouts and executes desktop launchers with isolated files and fake OS commands.</summary>
public sealed class PortablePackageTests
{
    private static string Bash => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe") : "/bin/bash";
    public static bool HasBash => File.Exists(Bash);

    [Theory]
    [InlineData("win-x64")]
    [InlineData("linux-x64")]
    [InlineData("linux-arm64")]
    [InlineData("osx-x64")]
    [InlineData("osx-arm64")]
    public void ArchiveKeepsPayloadBytesAndDesktopPermissions(string runtime)
    {
        var directory = Directory.CreateTempSubdirectory("athan-portable-");
        try
        {
            var publish = Directory.CreateDirectory(Path.Combine(directory.FullName, "publish"));
            File.WriteAllText(Path.Combine(publish.FullName, "AthanTime.Net"), "apphost fixture");
            File.WriteAllText(Path.Combine(publish.FullName, "System.Private.CoreLib.dll"), "runtime fixture");
            File.WriteAllText(Path.Combine(publish.FullName, "README.txt"), "User guide fixture");
            var plan = InstallerPlan.Create("portable", runtime, "1.2.3-beta.4", BuildHost.Windows);
            var archivePath = Path.Combine(directory.FullName, "test.zip");
            PortablePackage.Create(publish.FullName, archivePath, plan, RecipeRepository(directory.FullName));
            if (runtime != "win-x64")
            {
                using var stream = File.OpenRead(archivePath);
                using var binary = new BinaryReader(stream);
                stream.Seek(-6, SeekOrigin.End);
                var directoryOffset = binary.ReadUInt32();
                stream.Position = directoryOffset + 5;
                Assert.Equal(3, stream.ReadByte());
            }
            using var archive = ZipFile.OpenRead(archivePath);
            using (var guide = new StreamReader(archive.GetEntry("README.txt")!.Open()))
                Assert.Equal("User guide fixture", guide.ReadToEnd());
            Assert.Equal(0x81a4, (archive.GetEntry("README.txt")!.ExternalAttributes >> 16) & 65535);
            var mac = runtime.StartsWith("osx-", StringComparison.Ordinal);
            var prefix = mac ? "Athan Time.app/Contents/MacOS/" : "";
            using var reader = new StreamReader(archive.GetEntry(prefix + "System.Private.CoreLib.dll")!.Open());
            Assert.Equal("runtime fixture", reader.ReadToEnd());
            var app = archive.GetEntry(prefix + "AthanTime.Net")!;
            Assert.Equal(runtime == "win-x64" ? 0x81a4 : 0x81ed, (app.ExternalAttributes >> 16) & 65535);
            if (mac)
            {
                using var stream = archive.GetEntry("Athan Time.app/Contents/Info.plist")!.Open();
                var plist = XDocument.Load(stream);
                Assert.Contains(plist.Descendants("string"), item => item.Value == "Athan Time 1.2.3-beta.4");
                Assert.Equal(0x81ed, (archive.GetEntry("Launch-Athan-Time.command")!.ExternalAttributes >> 16) & 65535);
                Assert.Equal(0x81ed, (archive.GetEntry("Remove-My-Settings.command")!.ExternalAttributes >> 16) & 65535);
            }
            else if (runtime.StartsWith("linux-", StringComparison.Ordinal))
            {
                using var icon = new StreamReader(archive.GetEntry("athan-time.png")!.Open());
                Assert.Equal("Desktop icon fixture", icon.ReadToEnd());
                foreach (var name in new[] { "Launch-Athan-Time.sh", "Install-Shortcut.sh", "Install-Requirements.sh", "Remove-My-Settings.sh" })
                    Assert.Equal(0x81ed, (archive.GetEntry(name)!.ExternalAttributes >> 16) & 65535);
            }
            if (runtime != "win-x64") Assert.NotNull(archive.GetEntry("START-HERE.txt"));
            else Assert.NotNull(archive.GetEntry("Remove-My-Settings.cmd"));
        }
        finally { directory.Delete(true); }
    }

    [Fact(Skip = "Requires Bash", SkipUnless = nameof(HasBash))]
    public async Task LinuxLauncherSurvivesItsParentAndCreatesAUserMenuShortcut()
    {
        var directory = Directory.CreateTempSubdirectory("athan-linux-launch-");
        try
        {
            var folder = Path.Combine(directory.FullName, "app with spaces $cash%");
            Directory.CreateDirectory(folder);
            foreach (var name in new[] { "Launch-Athan-Time.sh", "Install-Shortcut.sh" })
                File.Copy(Path.Combine(AppContext.BaseDirectory, "ShellRecipes", "linux", name), Path.Combine(folder, name));
            File.WriteAllText(Path.Combine(folder, "athan-time.png"), "Desktop icon fixture");
            File.WriteAllText(Path.Combine(folder, "AthanTime.Net"), "#!/bin/bash\nfor i in {1..100}; do\n if [ -f release ]; then echo survived > completed; exit 0; fi\n sleep .05\ndone\nexit 1\n");
            await Run(directory.FullName, """
                uname() { echo Linux; }
                export -f uname
                bash "$APP/Install-Shortcut.sh"
                bash "$APP/Install-Shortcut.sh"
                bash "$APP/Launch-Athan-Time.sh"
                """, folder);
            // The launcher process has exited; release the independently running child afterwards.
            File.WriteAllText(Path.Combine(folder, "release"), "go");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!File.Exists(Path.Combine(folder, "completed"))) await Task.Delay(50, timeout.Token);
            var desktop = File.ReadAllText(Path.Combine(directory.FullName, "data", "applications", "athan-time.desktop"));
            Assert.Contains("Terminal=false", desktop);
            Assert.Contains("Icon=athan-time", desktop);
            Assert.Contains("StartupWMClass=" + typeof(AthanTime.Net.App).Assembly.GetName().Name, desktop);
            Assert.Equal("Desktop icon fixture", File.ReadAllText(Path.Combine(directory.FullName, "data", "icons", "hicolor", "256x256", "apps", "athan-time.png")));
            Assert.Contains("Launch-Athan-Time.sh", desktop);
            Assert.Contains("app with spaces", desktop);
            Assert.Contains("\\\\$cash%%", desktop);
        }
        finally { directory.Delete(true); }
    }

    [Fact(Skip = "Requires Bash", SkipUnless = nameof(HasBash))]
    public async Task MissingLinuxIconDoesNotInstallABrokenDesktopEntry()
    {
        var directory = Directory.CreateTempSubdirectory("athan-linux-missing-icon-");
        try
        {
            foreach (var name in new[] { "Launch-Athan-Time.sh", "Install-Shortcut.sh" })
                File.Copy(Path.Combine(AppContext.BaseDirectory, "ShellRecipes", "linux", name), Path.Combine(directory.FullName, name));
            File.WriteAllText(Path.Combine(directory.FullName, "AthanTime.Net"), "fixture; never executed");
            await Run(directory.FullName, """
                uname() { echo Linux; }
                export -f uname
                if bash "$APP/Install-Shortcut.sh"; then exit 1; fi
                test ! -e "$XDG_DATA_HOME/applications/athan-time.desktop"
                """, directory.FullName);
        }
        finally { directory.Delete(true); }
    }

    [Fact(Skip = "Requires Bash", SkipUnless = nameof(HasBash))]
    public async Task MacCommandPassesTheWholeBundlePathToLaunchServices()
    {
        var directory = Directory.CreateTempSubdirectory("athan-mac-launch-");
        try
        {
            var folder = Path.Combine(directory.FullName, "app with spaces");
            var executable = Path.Combine(folder, "Athan Time.app", "Contents", "MacOS");
            Directory.CreateDirectory(executable);
            File.WriteAllText(Path.Combine(executable, "AthanTime.Net"), "fixture");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "ShellRecipes", "macos", "Launch-Athan-Time.command"), Path.Combine(folder, "Launch-Athan-Time.command"));
            await Run(directory.FullName, """
                uname() { echo Darwin; }
                # exec cannot invoke an exported function, so place a fake open command on PATH.
                mkdir "$APP/mock-bin"
                printf '#!/bin/bash\nprintf "%%s" "$1" > "$APP/opened"\n' > "$APP/mock-bin/open"
                chmod +x "$APP/mock-bin/open"
                export -f uname
                export PATH="$APP/mock-bin:$PATH"
                bash "$APP/Launch-Athan-Time.command"
                """, folder);
            Assert.EndsWith("/Athan Time.app", File.ReadAllText(Path.Combine(folder, "opened")));
        }
        finally { directory.Delete(true); }
    }

    [Theory(Skip = "Requires Bash", SkipUnless = nameof(HasBash))]
    [InlineData("libicu70", "libssl3", "libasound2")]
    [InlineData("libicu78", "libssl3t64", "libasound2t64")]
    public async Task RequirementsHelperSelectsAvailableOsLibrariesWithoutInstallingDotnet(string icu, string ssl, string alsa)
    {
        var directory = Directory.CreateTempSubdirectory("athan-requirements-");
        try
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, "ShellRecipes", "linux", "Install-Requirements.sh"), Path.Combine(directory.FullName, "Install-Requirements.sh"));
            var wrapper = """
                uname() { echo Linux; }
                sudo() { "$@"; }
                apt-get() { printf '%s\n' "$*" >> "$APP/apt-commands"; }
                apt-cache() {
                  case "$2" in @ICU@|@SSL@|@ALSA@) return 0;; *) return 1;; esac
                }
                export -f uname sudo apt-get apt-cache
                bash "$APP/Install-Requirements.sh"
                """.Replace("@ICU@", icu).Replace("@SSL@", ssl).Replace("@ALSA@", alsa);
            await Run(directory.FullName, wrapper, directory.FullName);
            var commands = File.ReadAllLines(Path.Combine(directory.FullName, "apt-commands"));
            Assert.Equal("update", commands[0]);
            Assert.Contains(icu, commands[1]);
            Assert.Contains(ssl, commands[1]);
            Assert.Contains(alsa, commands[1]);
            Assert.DoesNotContain("dotnet", commands[1]);
        }
        finally { directory.Delete(true); }
    }

    [Theory(Skip = "Requires Bash", SkipUnless = nameof(HasBash))]
    [InlineData("cancel", 0)]
    [InlineData("REMOVE", 0)]
    [InlineData("REMOVE", 1)]
    public async Task SettingsRemovalHelperRequiresConsentAndReportsFailure(string answer, int result)
    {
        var directory = Directory.CreateTempSubdirectory("athan-remove-settings-");
        try
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, "ShellRecipes", "Remove-My-Settings.sh"), Path.Combine(directory.FullName, "Remove-My-Settings.sh"));
            File.WriteAllText(Path.Combine(directory.FullName, "AthanTime.Net"), "#!/bin/bash\nprintf '%s' \"$*\" > called\nexit " + result + "\n");
            await Run(directory.FullName, """
                uname() { echo Linux; }
                export -f uname
                cd "$APP"
                chmod +x "$APP/AthanTime.Net"
                if [[ $EUID == 0 ]]; then
                  if printf '%s\n' REMOVE | bash "$APP/Remove-My-Settings.sh"; then exit 1; fi
                  test ! -e called
                  exit 0
                fi
                status=0
                printf '%s\n' '@ANSWER@' | bash "$APP/Remove-My-Settings.sh" || status=$?
                test "$status" = '@STATUS@'
                if [[ '@ANSWER@' == REMOVE ]]; then
                  test "$(cat called)" = '--remove-user-settings'
                else
                  test ! -e called
                fi
                """.Replace("@ANSWER@", answer).Replace("@STATUS@", answer == "REMOVE" ? result.ToString() : "0"), directory.FullName);
        }
        finally { directory.Delete(true); }
    }

    private static string RecipeRepository(string directory)
    {
        var assets = Directory.CreateDirectory(Path.Combine(directory, "AthanTime.Net", "Assets"));
        File.WriteAllText(Path.Combine(assets.FullName, "athan-icon.png"), "Desktop icon fixture");
        var destination = Path.Combine(directory, "packaging");
        var source = Path.Combine(AppContext.BaseDirectory, "ShellRecipes");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        return directory;
    }

    private static async Task Run(string directory, string script, string folder)
    {
        var wrapper = Path.Combine(directory, "fixture.sh");
        File.WriteAllText(wrapper, "set -e\n" + """
            if command -v cygpath >/dev/null; then
              export APP="$(cygpath -u "$APP")"
              export XDG_STATE_HOME="$(cygpath -u "$XDG_STATE_HOME")"
              export XDG_DATA_HOME="$(cygpath -u "$XDG_DATA_HOME")"
            fi
            """.Replace("\r\n", "\n") + "\n" + script.Replace("\r\n", "\n"));
        var start = new ProcessStartInfo(Bash) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        start.ArgumentList.Add(wrapper.Replace('\\', '/'));
        start.Environment["APP"] = folder.Replace('\\', '/');
        start.Environment["XDG_STATE_HOME"] = Path.Combine(directory, "state").Replace('\\', '/');
        start.Environment["XDG_DATA_HOME"] = Path.Combine(directory, "data").Replace('\\', '/');
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)); }
        finally { if (!process.HasExited) process.Kill(true); }
        Assert.True(process.ExitCode == 0, await output + await error);
    }
}
