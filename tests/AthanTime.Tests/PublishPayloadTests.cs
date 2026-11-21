using System.Diagnostics;
using System.Xml.Linq;

namespace AthanTime.Tests;

/// <summary>Runs the actual publish guard against isolated payloads without launching the application.</summary>
public sealed class PublishPayloadTests
{
    /// <summary>Every supported package needs its runtime and offline native libraries.</summary>
    [Theory]
    [InlineData("win-x64", "", ".dll")]
    [InlineData("linux-x64", "lib", ".so")]
    [InlineData("linux-arm64", "lib", ".so")]
    [InlineData("osx-x64", "lib", ".dylib")]
    [InlineData("osx-arm64", "lib", ".dylib")]
    public async Task OfflinePayloadRequiresBundledRuntime(string runtime, string prefix, string suffix)
    {
        var directory = Directory.CreateTempSubdirectory("athan publish guard ").FullName;
        try
        {
            var files = new[]
            {
                "Assets/Audio/adhan.ogg", "Assets/Audio/takbeer.ogg",
                "NVorbis.dll", "SoundFlow.dll", "licenses/SOUNDFLOW-LICENSE.md", "licenses/SOUNDFLOW-THIRD-PARTY-NOTICES.txt",
                "Data/locations.db", "Data/desktop-control.json", "Data/movement-breaks.json", "Microsoft.Data.Sqlite.dll",
                "System.Private.CoreLib.dll", "AthanTime.Net.runtimeconfig.json", "README.txt",
                $"{prefix}miniaudio{suffix}", $"{prefix}e_sqlite3{suffix}",
                $"{prefix}coreclr{suffix}", $"{prefix}hostfxr{suffix}", $"libSkiaSharp{suffix}", $"libHarfBuzzSharp{suffix}"
            };
            foreach (var file in files)
            {
                var path = Path.Combine(directory, file);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, "Publish sentinel; never executed.", TestContext.Current.CancellationToken);
            }
            var passed = await RunGuard(directory, runtime);
            Assert.True(passed.ExitCode == 0, passed.Output);
            foreach (var missing in new[]
            {
                $"{prefix}coreclr{suffix}", $"{prefix}hostfxr{suffix}", $"{prefix}e_sqlite3{suffix}",
                $"libSkiaSharp{suffix}", $"libHarfBuzzSharp{suffix}", "licenses/SOUNDFLOW-THIRD-PARTY-NOTICES.txt",
                "Data/desktop-control.json", "Data/movement-breaks.json", "README.txt"
            })
            {
                var path = Path.Combine(directory, missing);
                File.Delete(path);
                var failed = await RunGuard(directory, runtime);
                Assert.NotEqual(0, failed.ExitCode);
                Assert.Contains("Missing offline deployment file:", failed.Output);
                Assert.Contains(missing, failed.Output);
                await File.WriteAllTextAsync(path, "Restored sentinel.", TestContext.Current.CancellationToken);
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    /// <summary>Unsupported deployment modes fail explicitly before an incomplete package can ship.</summary>
    [Theory]
    [InlineData("SelfContained", "false", "must be self-contained")]
    [InlineData("PublishSingleFile", "true", "require separate native validation")]
    [InlineData("PublishTrimmed", "true", "require separate native validation")]
    [InlineData("PublishAot", "true", "require separate native validation")]
    [InlineData("RuntimeIdentifier", "win-x86", "Unsupported publish runtime")]
    public async Task InvalidDeploymentModeFails(string property, string value, string message)
    {
        var directory = Directory.CreateTempSubdirectory("athan invalid publish ").FullName;
        try
        {
            var result = await RunGuard(directory, "win-x64", property, value);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains(message, result.Output);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static async Task<(int ExitCode, string Output)> RunGuard(string directory, string runtime,
        string? property = null, string? value = null)
    {
        var target = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "RecipeFiles", "App.csproj"))
            .Root!.Elements("Target").Single(element => (string?)element.Attribute("Name") == "VerifyOfflinePublish");
        var properties = new XElement("PropertyGroup",
            new XElement("RuntimeIdentifier", runtime), new XElement("SelfContained", "true"),
            new XElement("PublishDir", directory + Path.DirectorySeparatorChar));
        if (property is not null)
        {
            var element = properties.Element(property);
            if (element is null) properties.Add(new XElement(property, value));
            else element.Value = value!;
        }
        var project = Path.Combine(directory, "guard.proj");
        new XDocument(new XElement("Project", properties, target)).Save(project);
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { "msbuild", project, "-t:VerifyOfflinePublish", "-nologo", "-v:minimal" })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            return (process.ExitCode, await output + await error);
        }
        finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(); } }
    }
}
