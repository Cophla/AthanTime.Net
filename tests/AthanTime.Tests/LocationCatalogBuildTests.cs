using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace AthanTime.Tests;

/// <summary>Checks the versioned catalog archive and real incremental MSBuild extraction without native installers.</summary>
public sealed class LocationCatalogBuildTests
{
    [Fact]
    public void ArchiveContainsExactlyThePublishedDatabase()
    {
        using var archive = ZipFile.OpenRead(Path.Combine(AppContext.BaseDirectory, "ReferenceData", "locations.db.zip"));
        var entry = Assert.Single(archive.Entries);
        Assert.Equal("locations.db", entry.FullName);
        Assert.Equal(2000, entry.LastWriteTime.Year);
        using var compressed = entry.Open();
        using var published = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "locations.db"));
        Assert.Equal(published.Length, entry.Length);
        Assert.Equal(SHA256.HashData(published), SHA256.HashData(compressed));
        Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, "Data", "locations.db.zip")));
    }

    [Fact]
    public async Task ExtractionIsIncrementalAndRecoversChangedArchivesOrMissingOutputs()
    {
        var directory = Directory.CreateTempSubdirectory("athan-catalog-build-");
        try
        {
            var source = Path.Combine(directory.FullName, "catalog.zip");
            var project = Fixture(directory.FullName, source);
            var output = Path.Combine(directory.FullName, "obj", "location-catalog", "locations.db");
            Archive(source, "locations.db", "first");
            File.SetLastWriteTimeUtc(source, new DateTime(2010, 1, 1));
            var built = await Run(project);
            Assert.Equal(0, built.ExitCode);
            Assert.Equal("first", File.ReadAllText(output));
            Assert.Equal("Data/locations.db", File.ReadAllText(Path.Combine(directory.FullName, "mapping.txt")).Trim());
            // Fixed relative dates prove skipping without timing thresholds or sleeps.
            var marker = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(output, marker);
            Assert.Equal(0, (await Run(project)).ExitCode);
            Assert.Equal(marker, File.GetLastWriteTimeUtc(output));
            Archive(source, "locations.db", "other"); // Same entry length and ZIP-entry timestamp.
            File.SetLastWriteTimeUtc(source, new DateTime(2021, 1, 1));
            Assert.Equal(0, (await Run(project)).ExitCode);
            Assert.Equal("other", File.ReadAllText(output));
            File.Delete(output);
            Assert.Equal(0, (await Run(project)).ExitCode);
            Assert.Equal("other", File.ReadAllText(output));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("corrupt")]
    [InlineData("wrong-entry")]
    public async Task InvalidArchiveFailsVisiblyInsteadOfCreatingAnEmptyCatalog(string kind)
    {
        var directory = Directory.CreateTempSubdirectory("athan-invalid-catalog-");
        try
        {
            var source = Path.Combine(directory.FullName, "catalog.zip");
            if (kind == "corrupt") File.WriteAllText(source, "not a ZIP");
            if (kind == "wrong-entry") Archive(source, "other.db", "wrong");
            var result = await Run(Fixture(directory.FullName, source));
            Assert.NotEqual(0, result.ExitCode);
            if (kind == "missing") Assert.Contains("Missing offline catalog archive", result.Output);
            if (kind == "wrong-entry") Assert.Contains("must contain locations.db", result.Output);
            Assert.False(File.Exists(Path.Combine(directory.FullName, "obj", "location-catalog", "locations.db")));
        }
        finally { directory.Delete(recursive: true); }
    }

    private static void Archive(string path, string name, string text)
    {
        File.Delete(path);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = archive.CreateEntry(name);
        entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(text);
    }

    private static string Fixture(string directory, string archive)
    {
        var path = Path.Combine(directory, "catalog.proj");
        new XDocument(new XElement("Project",
            new XElement("PropertyGroup",
                new XElement("IntermediateOutputPath", Path.Combine(directory, "obj") + Path.DirectorySeparatorChar),
                new XElement("LocationCatalogArchive", archive)),
            new XElement("Import", new XAttribute("Project", Path.Combine(AppContext.BaseDirectory, "ReferenceData", "LocationCatalog.targets"))),
            new XElement("Target", new XAttribute("Name", "AssignTargetPaths"),
                new XElement("WriteLinesToFile", new XAttribute("File", Path.Combine(directory, "mapping.txt")),
                    new XAttribute("Lines", "@(Content->'%(Link)')"), new XAttribute("Overwrite", "true")))))
            .Save(path);
        return path;
    }

    private static async Task<(int ExitCode, string Output)> Run(string project)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(project)!
        } };
        foreach (var argument in new[] { "msbuild", project, "-nologo", "-verbosity:minimal", "-target:AssignTargetPaths" })
            process.StartInfo.ArgumentList.Add(argument);
        process.StartInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        process.StartInfo.Environment["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        process.Start();
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return (process.ExitCode, await output + await error);
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }
}
