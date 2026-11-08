using System.Diagnostics;

namespace AthanTime.Tests;

/// <summary>Runs the launchers against a fake dotnet command, never an OS installer.</summary>
public sealed class InstallerLauncherTests
{
    /// <summary>Default and explicit requests preserve paths, arguments and failure exit codes.</summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(0, false)]
    [InlineData(7, false)]
    public async Task LaunchersUseTheirOwnRepositoryAndPreserveCommandResults(int exitCode, bool defaultRequest)
    {
        var directory = Directory.CreateTempSubdirectory("athan launcher tests ").FullName;
        try
        {
            var repository = Path.Combine(directory, "repository with spaces");
            var caller = Path.Combine(directory, "unrelated caller");
            var commands = Path.Combine(directory, "fake commands");
            Directory.CreateDirectory(repository);
            Directory.CreateDirectory(caller);
            Directory.CreateDirectory(commands);
            foreach (var source in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "LauncherFiles")))
                File.Copy(source, Path.Combine(repository, Path.GetFileName(source)));

            // A batch command intentionally requires CALL to return control to the Windows launcher.
            var fakeDotnet = Path.Combine(commands, OperatingSystem.IsWindows() ? "dotnet.cmd" : "dotnet");
            File.WriteAllText(fakeDotnet, OperatingSystem.IsWindows()
                ? $"@echo off\r\ncd\r\n:next\r\nif \"%~1\"==\"\" exit /b {exitCode}\r\necho [%~1]\r\nshift\r\ngoto next\r\n"
                : $"#!/bin/bash\nprintf '%s\\n' \"$PWD\"\nprintf '[%s]\\n' \"$@\"\nexit {exitCode}\n");
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(fakeDotnet, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            string[] launchers = OperatingSystem.IsWindows()
                ? ["Create-Installers.cmd"] : ["Create-Installers.sh", "Create-Installers.command"];
            foreach (var launcher in launchers)
            {
                var path = Path.Combine(repository, launcher);
                var start = new ProcessStartInfo
                {
                    FileName = OperatingSystem.IsWindows()
                        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe") : "/bin/bash",
                    WorkingDirectory = caller,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                if (OperatingSystem.IsWindows())
                    start.Arguments = $"/d /s /c \"\"{path}\"{(defaultRequest ? "" : " --version 1.2.3 --dry-run")}\"";
                else
                {
                    start.ArgumentList.Add(path);
                    if (!defaultRequest)
                        foreach (var argument in new[] { "--version", "1.2.3", "--dry-run" })
                            start.ArgumentList.Add(argument);
                }
                start.Environment["PATH"] = commands + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
                using var process = Process.Start(start)!;
                process.StandardInput.Close();
                var outputTask = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
                var errorTask = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
                await process.WaitForExitAsync(TestContext.Current.CancellationToken);
                var output = await outputTask + await errorTask;
                Assert.Equal(exitCode, process.ExitCode);
                Assert.Contains(repository, output);
                Assert.DoesNotContain(caller, output);
                Assert.Contains("[run]", output);
                Assert.Contains($"[{Path.Combine(repository, "tools", "InstallerBuilder", "InstallerBuilder.csproj")}]", output);
                Assert.Contains("[Release]", output);
                if (!defaultRequest)
                {
                    Assert.Contains("[--version]", output);
                    Assert.Contains("[1.2.3]", output);
                    Assert.Contains("[--dry-run]", output);
                }
                Assert.Contains(exitCode == 0 ? "Packaging command completed" : "failed with exit code 7", output);
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
