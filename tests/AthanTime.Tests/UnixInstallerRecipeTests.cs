using System.Diagnostics;
using System.Xml.Linq;

namespace AthanTime.Tests;

/// <summary>Exercises Bash recipes with simulated hosts and fake native commands, never OS installers.</summary>
public sealed class UnixInstallerRecipeTests
{
    private static string Bash => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe") : "/bin/bash";

    /// <summary>Windows shell coverage uses Git Bash; matching native hosts use system Bash.</summary>
    public static bool HasBash => File.Exists(Bash);

    /// <summary>Direct recipes choose shared version/host architecture and reject invalid versions or hosts before publishing.</summary>
    [Theory(Skip = "Requires Bash (Git Bash on Windows)", SkipUnless = nameof(HasBash))]
    [InlineData("linux", "Linux", "x86_64", "", true)]
    [InlineData("linux", "Linux", "aarch64", "", true)]
    [InlineData("macos", "Darwin", "x86_64", "", true)]
    [InlineData("macos", "Darwin", "arm64", "", true)]
    [InlineData("linux", "Linux", "x86_64", "2.3.4", true)]
    [InlineData("macos", "Darwin", "arm64", "2.3.4", true)]
    [InlineData("linux", "Linux", "x86_64", "../bad", false)]
    [InlineData("linux", "Linux", "x86_64", "1.0.2-beta.1", true)]
    [InlineData("linux", "Linux", "aarch64", "1.0.2-beta.1", true)]
    [InlineData("macos", "Darwin", "x86_64", "1.0.2-beta.1", true)]
    [InlineData("macos", "Darwin", "arm64", "1.0.2-beta.1", true)]
    [InlineData("macos", "Darwin", "arm64", "1.2-beta.1", false)]
    [InlineData("macos", "Darwin", "arm64", "1.2.3.4", false)]
    [InlineData("linux", "Darwin", "x86_64", "", false)]
    [InlineData("macos", "Linux", "x86_64", "", false)]
    public async Task DirectRecipesUseSharedMetadataAndMatchingHost(string recipe, string host, string architecture, string version, bool success)
    {
        var directory = Directory.CreateTempSubdirectory("athan shell recipe tests ").FullName;
        try
        {
            var repository = Directory.CreateDirectory(Path.Combine(directory, "repository with spaces")).FullName;
            var sources = Path.Combine(AppContext.BaseDirectory, "ShellRecipes");
            foreach (var source in Directory.EnumerateFiles(sources, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(repository, "packaging", Path.GetRelativePath(sources, source));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target);
            }
            File.WriteAllText(Path.Combine(repository, "Directory.Build.props"), "<Project><PropertyGroup><Version>9.8.7</Version></PropertyGroup></Project>");
            var assets = Directory.CreateDirectory(Path.Combine(repository, "AthanTime.Net", "Assets"));
            File.WriteAllText(Path.Combine(assets.FullName, "athan-icon.png"), "Icon fixture; never rendered.");
            var wrapper = Path.Combine(directory, "verify.sh");
            File.WriteAllText(wrapper, """
                #!/usr/bin/env bash
                set -euo pipefail
                cd -- "$1"
                export ATHAN_FAKE_OS="$2" ATHAN_FAKE_CPU="$3"
                export TMPDIR="$PWD/temporary staging"
                mkdir -p "$TMPDIR"
                uname() { if [[ "${1:-}" == '-s' ]]; then printf '%s\n' "$ATHAN_FAKE_OS"; else printf '%s\n' "$ATHAN_FAKE_CPU"; fi; }
                dotnet() {
                  printf '[%s]\n' "$@" >> "$PWD/publish-arguments.txt"
                  local output=''
                  while [[ $# -gt 0 ]]; do
                    if [[ "$1" == '-o' ]]; then output="$2"; shift; fi
                    shift
                  done
                  mkdir -p "$output"
                  printf '%s' 'Payload fixture; never executed.' > "$output/AthanTime.Net"
                }
                dpkg-deb() {
                  local stage="${@: -2:1}" output="${@: -1}"
                  cp "$stage/DEBIAN/control" "$PWD/observed-control"
                  cp "$stage/usr/share/applications/athan-time.desktop" "$PWD/observed-desktop"
                  printf '%s' 'Fake DEB output; never installed.' > "$output"
                }
                hdiutil() {
                  local output="${@: -1}" stage=''
                  while [[ $# -gt 0 ]]; do
                    if [[ "$1" == '-srcfolder' ]]; then stage="$2"; shift; fi
                    shift
                  done
                  cp "$stage/Athan Time.app/Contents/Info.plist" "$PWD/observed-info.plist"
                  printf '%s' 'Fake DMG output; never mounted.' > "$output"
                }
                # Link creation is an OS smoke check; Git Bash cannot create all Unix dangling links.
                ln() { printf '%s' 'Link fixture' > "${@: -1}"; }
                export -f uname dotnet dpkg-deb hdiutil ln
                if [[ "$4" == 'linux' ]]; then script='packaging/linux/build-deb.sh'; else script='packaging/macos/build-app.sh'; fi
                bash "$script" "$5"
                """.Replace("\r\n", "\n"));
            var start = new ProcessStartInfo(Bash)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var argument in new[] { wrapper.Replace('\\', '/'), repository.Replace('\\', '/'), host, architecture, recipe, version })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            try
            {
                var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
                var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
                await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
                Assert.True(success ? process.ExitCode == 0 : process.ExitCode != 0, await output + await error);
            }
            finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(); } }

            var publishArguments = Path.Combine(repository, "publish-arguments.txt");
            if (!success) { Assert.False(File.Exists(publishArguments)); return; }
            var expectedVersion = version.Length == 0 ? "9.8.7" : version;
            var arm = architecture is "arm64" or "aarch64";
            var rid = recipe == "linux" ? (arm ? "linux-arm64" : "linux-x64") : (arm ? "osx-arm64" : "osx-x64");
            var arguments = File.ReadAllText(publishArguments);
            Assert.Contains("[--self-contained]\n[true]", arguments);
            Assert.Contains("[-c]\n[Release]", arguments);
            Assert.Contains("[-p:PublishSingleFile=false]", arguments);
            Assert.Contains($"[{rid}]", arguments);
            Assert.Contains($"[-p:Version={expectedVersion}]", arguments);
            var filename = recipe == "linux" ? $"AthanTime-{expectedVersion}-{(arm ? "arm64" : "amd64")}.deb" : $"AthanTime-{expectedVersion}-{rid}.dmg";
            Assert.True(File.Exists(Path.Combine(repository, "artifacts", "packages", filename)));
            if (recipe == "linux")
            {
                var control = File.ReadAllText(Path.Combine(repository, "observed-control"));
                Assert.Contains($"Version: {expectedVersion.Replace('-', '~')}", control);
                Assert.Contains($"Architecture: {(arm ? "arm64" : "amd64")}", control);
                Assert.Contains($"Release: {expectedVersion}.", control);
                Assert.Contains($"Name=Athan Time {expectedVersion}", File.ReadAllText(Path.Combine(repository, "observed-desktop")));
            }
            else
            {
                var plist = XDocument.Load(Path.Combine(repository, "observed-info.plist"));
                string Value(string key) => plist.Descendants("key").Single(element => element.Value == key).ElementsAfterSelf("string").First().Value;
                Assert.Equal(expectedVersion.Split('-')[0], Value("CFBundleVersion"));
                Assert.Equal(expectedVersion.Split('-')[0], Value("CFBundleShortVersionString"));
                Assert.Equal($"Athan Time {expectedVersion}", Value("CFBundleDisplayName"));
                Assert.Equal($"Athan Time {expectedVersion}", Value("CFBundleGetInfoString"));
            }
            Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(repository, "temporary staging")));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
