using System.Diagnostics;

namespace AthanTime.Tests;

/// <summary>Protects Windows scope, staging and exact-removal contracts without running installers.</summary>
public sealed class WindowsInstallerRecipeTests
{
    /// <summary>Setup and installed-app registration expose prerelease identity without changing the upgrade key.</summary>
    [Fact]
    public void SetupAndUninstallDisplayTheFullReleaseVersion()
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RecipeFiles", "AthanTime.nsi"));
        Assert.Contains("Name \"Athan Time ${APP_VERSION}\"", script);
        Assert.Contains("\"DisplayName\" \"Athan Time ${APP_VERSION}\"", script);
        Assert.Contains("\"DisplayVersion\" \"${APP_VERSION}\"", script);
        Assert.Contains("Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\AthanTime", script);
    }

    /// <summary>Windows PowerShell is the Windows recipe's prerequisite.</summary>
    public static bool IsWindows => OperatingSystem.IsWindows();

    /// <summary>Both scopes show their destination; fresh installs can browse and updates visibly retain their path.</summary>
    [Fact]
    public void DestinationPageShowsDefaultsAndLocksRegisteredUpdateFolder()
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RecipeFiles", "AthanTime.nsi")).ReplaceLineEndings("\n");
        Assert.Contains("Page directory DirectoryPre DirectoryShow DirectoryLeave", script);
        Assert.Contains("DirText \"Setup will install Athan Time in the folder below.", script);
        Assert.Contains("InstallDir \"\"", script);
        var initialization = ScriptBlock(script, "Function .onInit", "FunctionEnd");
        Assert.Contains("StrCpy $RequestedDirectory $INSTDIR", initialization);
        Assert.DoesNotContain("${GetOptions} $0 \"/D=\"", initialization);
        var validation = ScriptBlock(script, "Function ValidateTargetScope", "FunctionEnd");
        Assert.Contains("GetFullPathName $0 \"$INSTDIR\\\"", validation);
        Assert.DoesNotContain("GetFullPathName $INSTDIR", validation);
        Assert.True(validation.IndexOf("IfErrors blocked", StringComparison.Ordinal) < validation.IndexOf("StrCpy $INSTDIR $0 -1", StringComparison.Ordinal));
        var prepare = ScriptBlock(script, "Function DirectoryPre", "FunctionEnd");
        Assert.DoesNotContain("Abort", prepare);
        Assert.Contains("StrCpy $INSTDIR $ExistingInstallDirectory", prepare);
        var show = ScriptBlock(script, "Function DirectoryShow", "FunctionEnd");
        var branches = show.Split("${Else}", StringSplitOptions.None);
        Assert.Equal(2, branches.Length);
        Assert.Contains("$ExistingInstallDirectory == \"\"", branches[0]);
        Assert.Contains("${EM_SETREADONLY} 0 0", branches[0]);
        Assert.Contains("EnableWindow $2 1", branches[0]);
        Assert.Contains("${EM_SETREADONLY} 1 0", branches[1]);
        Assert.Contains("EnableWindow $2 0", branches[1]);
        var leave = ScriptBlock(script, "Function DirectoryLeave", "FunctionEnd");
        Assert.Contains("StrCpy $INSTDIR $ExistingInstallDirectory", leave);
    }

    /// <summary>Protects scope boundaries: shared registration follows scope, while login cleanup stays user-only.</summary>
    [Fact]
    public void InstallerKeepsMachineRegistrationSeparateFromUserPreferences()
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RecipeFiles", "AthanTime.nsi")).ReplaceLineEndings("\n");
        var context = ScriptBlock(script, "Function ConfigureInstallScope", "FunctionEnd");
        Assert.Contains("SetShellVarContext current\n  SetRegView 32", context);
        Assert.Contains("SetShellVarContext all\n  SetRegView 64", context);
        Assert.Contains("$LOCALAPPDATA\\Programs\\Athan Time", context);
        Assert.Contains("$PROGRAMFILES64\\Athan Time", context);
        var install = ScriptBlock(script, "Section \"Athan Time\" MainSection", "SectionEnd");
        Assert.Contains("WriteRegStr SHCTX", install);
        Assert.DoesNotContain("WriteRegStr HKCU", install);
        Assert.DoesNotContain("WriteRegStr HKLM", install);
        Assert.DoesNotContain("CurrentVersion\\Run", install);
        var uninstall = ScriptBlock(script, "Section \"Uninstall\"", "SectionEnd");
        var login = uninstall.IndexOf("ReadRegStr $0 HKCU", StringComparison.Ordinal);
        var registration = uninstall.IndexOf("registration:", StringComparison.Ordinal);
        Assert.InRange(login, 0, registration - 1);
        Assert.Contains("StrCmp $InstallScope \"CurrentUser\" 0 registration", uninstall[..login]);
        Assert.Contains("--start-minimized' 0 +2", uninstall[login..registration]);
        Assert.Contains("DeleteRegKey SHCTX", uninstall[registration..]);
        Assert.Contains("0 removePayload", uninstall[registration..]);
        Assert.DoesNotContain("RMDir /r", uninstall);
        Assert.DoesNotContain("$APPDATA", uninstall);
        Assert.DoesNotContain("$PROFILE", uninstall);
    }

    /// <summary>Only machine operations may elevate; their exit codes and uninstaller lifetime must be retained.</summary>
    [Fact]
    public void InstallerElevatesOnlyMachineOperationsAndWaitsForTheirResult()
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RecipeFiles", "AthanTime.nsi")).ReplaceLineEndings("\n");
        Assert.Contains("RequestExecutionLevel user", script);
        Assert.DoesNotContain("RequestExecutionLevel admin", script);
        // Preparation must remain read-only: a machine update may start without admin permission.
        // Actual UAC and registry effects remain disposable native smoke checks.
        var preparation = ScriptBlock(script, "Function ConfigureInstallScope", "FunctionEnd");
        Assert.DoesNotContain("Call EnsureAppClosed", preparation);
        Assert.DoesNotContain("Call RequireMachinePrivileges", preparation);
        var initialization = ScriptBlock(script, "Function .onInit", "FunctionEnd");
        Assert.Contains("IfSilent applyScope\n  StrCmp $ScopeRequested \"\" done\napplyScope:", initialization);
        var choice = ScriptBlock(script, "Function ScopePage", "FunctionEnd");
        Assert.Contains("Only me", choice);
        Assert.Contains("All users", choice);
        Assert.DoesNotContain("$ExistingInstallDirectory", choice);
        var elevation = ScriptBlock(script, "Function RequireMachinePrivileges", "FunctionEnd");
        Assert.StartsWith("\n  StrCmp $InstallScope \"AllUsers\" 0 done", elevation);
        Assert.Contains("StrCmp $0 \"Admin\" done", elevation);
        Assert.Contains("SetErrorLevel $0\n  Quit", elevation);
        var relay = ScriptBlock(script, "Function ${PREFIX}RunElevated", "FunctionEnd");
        Assert.Contains("ShellExecuteExW", relay);
        Assert.Contains("i.r0 ?e'\n  Pop $5", relay);
        Assert.Contains("WaitForSingleObject", relay);
        Assert.Contains("GetExitCodeProcess", relay);
        Assert.Contains("CloseHandle", relay);
        var uninstall = ScriptBlock(script, "Function un.onInit", "FunctionEnd");
        var machine = uninstall[uninstall.IndexOf("machine:", StringComparison.Ordinal)..uninstall.IndexOf("current:", StringComparison.Ordinal)];
        Assert.Contains("$PLUGINSDIR\\AthanUninstall.exe", machine);
        Assert.Contains("_?=$INSTDIR", machine);
        Assert.Contains("Call un.RunElevated\n  SetErrorLevel $0\n  Quit", machine);
        Assert.DoesNotContain("RunElevated", uninstall[uninstall.IndexOf("current:", StringComparison.Ordinal)..]);
    }

    /// <summary>Scope belongs to the install directory; registered legacy updates retain their original folder.</summary>
    [Fact]
    public void InstallerPreservesLegacyUpdatesAndRejectsCrossScopeFolderReuse()
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RecipeFiles", "AthanTime.nsi")).ReplaceLineEndings("\n");
        var context = ScriptBlock(script, "Function ConfigureInstallScope", "FunctionEnd");
        Assert.Contains("ReadRegStr $0 SHCTX", context);
        Assert.Contains("${GetParent} $0 $ExistingInstallDirectory", context);
        Assert.True(context.IndexOf("StrCpy $INSTDIR $RequestedDirectory", StringComparison.Ordinal)
            < context.IndexOf("StrCpy $INSTDIR $ExistingInstallDirectory", StringComparison.Ordinal));
        var guard = ScriptBlock(script, "Function ValidateTargetScope", "FunctionEnd");
        Assert.Contains("StrCmp $0 $InstallScope done blocked", guard);
        Assert.Contains("StrCmp $ExistingInstallDirectory $INSTDIR done", guard);
        Assert.Contains("SetErrorLevel 2\n  Abort", guard);
        var install = ScriptBlock(script, "Section \"Athan Time\" MainSection", "SectionEnd");
        Assert.True(install.IndexOf("Call ValidateTargetScope", StringComparison.Ordinal) < install.IndexOf("File /r", StringComparison.Ordinal));
        Assert.Contains("WriteINIStr \"$INSTDIR\\athan-install.ini\" \"Install\" \"Scope\" \"$InstallScope\"", install);
        var uninstall = ScriptBlock(script, "Function un.onInit", "FunctionEnd");
        Assert.Contains("ReadINIStr $InstallScope \"$INSTDIR\\athan-install.ini\"", uninstall);
        Assert.Contains("StrCmp $InstallScope \"\" 0 +2\n  StrCpy $InstallScope \"CurrentUser\"", uninstall);
    }

    private static string ScriptBlock(string script, string start, string end)
    {
        var first = script.IndexOf(start + "\n", StringComparison.Ordinal);
        Assert.True(first >= 0, $"Missing script block: {start}");
        first += start.Length;
        var last = script.IndexOf("\n" + end, first, StringComparison.Ordinal);
        Assert.True(last >= 0, $"Unclosed script block: {start}");
        return script[first..last];
    }

    /// <summary>Only published files enter the uninstall list; shared version and staging cleanup are preserved.</summary>
    [Fact(Skip = "Requires Windows PowerShell", SkipUnless = nameof(IsWindows))]
    public async Task RecipeBuildsAnExactUninstallListAndCleansItsStaging()
    {
        var directory = Directory.CreateTempSubdirectory("athan recipe tests ").FullName;
        try
        {
            var repository = Path.Combine(directory, "repository");
            var recipes = Path.Combine(repository, "packaging", "windows");
            Directory.CreateDirectory(recipes);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "RecipeFiles", "build-installer.ps1"),
                Path.Combine(recipes, "build-installer.ps1"));
            File.WriteAllText(Path.Combine(repository, "Directory.Build.props"),
                "<Project><PropertyGroup><Version>9.8.7</Version></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(repository, "user-owned.ogg"), "Never part of a published payload.");
            File.WriteAllText(Path.Combine(directory, "fake-compiler.ps1"), """
                $ErrorActionPreference = 'Stop'
                if ($args -notcontains '/DAPP_VERSION=9.8.7') { throw 'Shared version was not used.' }
                $include = ($args | Where-Object { $_.StartsWith('/DUNINSTALL_INCLUDE=') }).Substring(20)
                Copy-Item -LiteralPath $include -Destination (Join-Path $PSScriptRoot 'manifest.nsh')
                $global:LASTEXITCODE = 0
                """);
            var wrapper = Path.Combine(directory, "verify.ps1");
            File.WriteAllText(wrapper, """
                $ErrorActionPreference = 'Stop'
                function dotnet {
                    [IO.File]::WriteAllLines((Join-Path $PSScriptRoot 'publish-arguments.txt'), [string[]]$args)
                    $publish = $args[[Array]::IndexOf($args, '-o') + 1]
                    $audio = Join-Path $publish 'Assets\Audio'
                    New-Item -ItemType Directory -Path $audio -Force | Out-Null
                    [IO.File]::WriteAllText((Join-Path $publish 'AthanTime.Net.exe'), 'Published executable sentinel')
                    [IO.File]::WriteAllText((Join-Path $audio 'bundled $tone.ogg'), 'Published audio sentinel')
                    $global:LASTEXITCODE = 0
                }
                & (Join-Path $PSScriptRoot 'repository\packaging\windows\build-installer.ps1') `
                    -NsisCompiler (Join-Path $PSScriptRoot 'fake-compiler.ps1')
                """);
            var start = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.Environment.Remove("ATHAN_SIGNING_CERTIFICATE_THUMBPRINT");
            start.Environment.Remove("ATHAN_REQUIRE_SIGNING");
            foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", wrapper })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            try
            {
                var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
                var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
                await process.WaitForExitAsync(TestContext.Current.CancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
                Assert.True(process.ExitCode == 0, await output + await error);
            }
            finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(); } }

            AssertOfflinePublishArguments(File.ReadAllLines(Path.Combine(directory, "publish-arguments.txt")));
            var manifest = File.ReadAllText(Path.Combine(directory, "manifest.nsh"));
            Assert.Contains("Delete \"$INSTDIR\\AthanTime.Net.exe\"", manifest);
            Assert.Contains("Delete \"$INSTDIR\\Assets\\Audio\\bundled $$tone.ogg\"", manifest);
            Assert.DoesNotContain("user-owned.ogg", manifest);
            Assert.DoesNotContain("RMDir /r", manifest);
            Assert.True(manifest.IndexOf("RMDir \"$INSTDIR\\Assets\\Audio\"", StringComparison.Ordinal)
                < manifest.IndexOf("RMDir \"$INSTDIR\\Assets\"", StringComparison.Ordinal));
            Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(repository, "artifacts", "publish")));
            Assert.True(File.Exists(Path.Combine(repository, "user-owned.ogg")));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    /// <summary>A failed final signature cannot replace an existing package with an unsigned artifact.</summary>
    [Theory(Skip = "Requires Windows PowerShell", SkipUnless = nameof(IsWindows))]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SignedPackageIsPublishedOnlyAfterSigningCompletes(bool failFinalSigning)
    {
        var directory = Directory.CreateTempSubdirectory("athan signed recipe tests ").FullName;
        try
        {
            var recipes = Path.Combine(directory, "packaging", "windows");
            Directory.CreateDirectory(recipes);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "RecipeFiles", "build-installer.ps1"), Path.Combine(recipes, "build-installer.ps1"));
            File.WriteAllText(Path.Combine(directory, "Directory.Build.props"), "<Project><PropertyGroup><Version>9.8.7</Version></PropertyGroup></Project>");
            var packages = Directory.CreateDirectory(Path.Combine(directory, "artifacts", "packages"));
            var package = Path.Combine(packages.FullName, "AthanTime-9.8.7-win-x64-setup.exe");
            File.WriteAllText(package, "previous package");
            File.WriteAllText(Path.Combine(recipes, "Sign-Code.ps1"), """
                param([string]$Path, [switch]$CheckOnly)
                $phase = if ($CheckOnly) { 'preflight' } elseif (Test-Path -LiteralPath $Path -PathType Container) { 'payload' } else { 'package' }
                [IO.File]::AppendAllText((Join-Path $PSScriptRoot '../../phases.txt'), ($phase + [Environment]::NewLine))
                if ($phase -eq 'package' -and $env:FAKE_SIGN_FAILURE -eq '1') { throw 'Simulated final signature failure.' }
                """);
            File.WriteAllText(Path.Combine(directory, "fake-compiler.ps1"), """
                if (-not ($args | Where-Object { $_.StartsWith('/DSIGN_SCRIPT=') })) { throw 'Uninstaller signing hook was omitted.' }
                $output = ($args | Where-Object { $_.StartsWith('/DOUTPUT_DIR=') }).Substring(13)
                [IO.File]::AppendAllText((Join-Path $PSScriptRoot 'phases.txt'), ('compile' + [Environment]::NewLine))
                [IO.File]::WriteAllText((Join-Path $output 'AthanTime-9.8.7-win-x64-setup.exe'), 'new package')
                $global:LASTEXITCODE = 0
                """);
            var wrapper = Path.Combine(directory, "verify.ps1");
            File.WriteAllText(wrapper, """
                $ErrorActionPreference = 'Stop'
                function dotnet {
                    [IO.File]::WriteAllLines((Join-Path $PSScriptRoot 'publish-arguments.txt'), [string[]]$args)
                    $publish = $args[[Array]::IndexOf($args, '-o') + 1]
                    New-Item -ItemType Directory -Path $publish -Force | Out-Null
                    [IO.File]::WriteAllText((Join-Path $publish 'AthanTime.Net.exe'), 'Published sentinel; never executed.')
                    $global:LASTEXITCODE = 0
                }
                & (Join-Path $PSScriptRoot 'packaging/windows/build-installer.ps1') `
                    -NsisCompiler (Join-Path $PSScriptRoot 'fake-compiler.ps1')
                """);
            var start = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.Environment["ATHAN_SIGNING_CERTIFICATE_THUMBPRINT"] = new string('A', 40);
            start.Environment.Remove("ATHAN_REQUIRE_SIGNING");
            start.Environment["FAKE_SIGN_FAILURE"] = failFinalSigning ? "1" : "0";
            foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", wrapper }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            try
            {
                var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
                var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
                await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
                var result = await output + await error;
                Assert.True(failFinalSigning ? process.ExitCode != 0 : process.ExitCode == 0, result);
            }
            finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(); } }
            AssertOfflinePublishArguments(File.ReadAllLines(Path.Combine(directory, "publish-arguments.txt")));
            Assert.Equal(new[] { "preflight", "payload", "compile", "package" }, File.ReadAllLines(Path.Combine(directory, "phases.txt")));
            Assert.Equal(failFinalSigning ? "previous package" : "new package", File.ReadAllText(package));
            Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(directory, "artifacts", "publish")));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    /// <summary>The legacy wrapper forwards explicit options and leaves omitted version/RID selection to the shared builder.</summary>
    [Theory(Skip = "Requires Windows PowerShell", SkipUnless = nameof(IsWindows))]
    [InlineData("", "")]
    [InlineData("linux-arm64", "2.3.4")]
    [InlineData("osx-x64", "")]
    public async Task PortableWrapperDoesNotInjectStaleDefaults(string runtime, string version)
    {
        var directory = Directory.CreateTempSubdirectory("athan portable recipe tests ").FullName;
        try
        {
            var packaging = Directory.CreateDirectory(Path.Combine(directory, "packaging"));
            File.Copy(Path.Combine(AppContext.BaseDirectory, "RecipeFiles", "build-portable.ps1"), Path.Combine(packaging.FullName, "build-portable.ps1"));
            var wrapper = Path.Combine(directory, "verify.ps1");
            File.WriteAllText(wrapper, """
                param([string]$Runtime, [string]$Version)
                $ErrorActionPreference = 'Stop'
                function dotnet {
                    [IO.File]::WriteAllLines((Join-Path $PSScriptRoot 'arguments.txt'), [string[]]$args)
                    $global:LASTEXITCODE = 0
                }
                $options = @{}
                if ($Runtime) { $options.Runtime = $Runtime }
                if ($Version) { $options.Version = $Version }
                & (Join-Path $PSScriptRoot 'packaging/build-portable.ps1') @options
                """);
            var start = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = Path.GetTempPath()
            };
            foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", wrapper, "-Runtime", runtime, "-Version", version }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            try
            {
                var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
                var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
                await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
                Assert.True(process.ExitCode == 0, await output + await error);
            }
            finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(); } }
            var arguments = File.ReadAllLines(Path.Combine(directory, "arguments.txt"));
            Assert.Contains(Path.Combine(directory, "tools", "InstallerBuilder"), arguments);
            Assert.Contains("portable", arguments);
            Assert.Equal(runtime.Length != 0, arguments.Contains("--rid", StringComparer.Ordinal));
            Assert.Equal(version.Length != 0, arguments.Contains("--version", StringComparer.Ordinal));
            if (runtime.Length != 0) Assert.Contains(runtime, arguments);
            if (version.Length != 0) Assert.Contains(version, arguments);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void AssertOfflinePublishArguments(string[] arguments)
    {
        Assert.Equal("publish", arguments[0]);
        var selfContained = Array.IndexOf(arguments, "--self-contained");
        Assert.InRange(selfContained, 0, arguments.Length - 2);
        Assert.Equal("true", arguments[selfContained + 1]);
        var configuration = Array.IndexOf(arguments, "-c");
        Assert.InRange(configuration, 0, arguments.Length - 2);
        Assert.Equal("Release", arguments[configuration + 1]);
        // PowerShell functions split colon arguments; native dotnet receives the original property argument.
        var singleFile = Array.IndexOf(arguments, "PublishSingleFile=false");
        Assert.InRange(singleFile, 1, arguments.Length - 1);
        Assert.Equal("-p:", arguments[singleFile - 1]);
    }
}
