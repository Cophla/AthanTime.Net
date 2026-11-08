using System.IO.Compression;
using System.Security.Cryptography;

namespace InstallerBuilder;

/// <summary>Finds installed NSIS or prepares a verified, repository-local Windows build tool.</summary>
public sealed class NsisCompiler(HttpClient client)
{
    /// <summary>The pinned portable compiler release; updates require checking its published checksum.</summary>
    public const string Version = "3.13";
    /// <summary>Official NSIS portable archive, used only by the Windows packaging tool.</summary>
    public const string DownloadUrl = "https://downloads.sourceforge.net/project/nsis/NSIS%203/3.13/nsis-3.13.zip";
    /// <summary>SHA-256 published on the official SourceForge download page.</summary>
    public const string ArchiveSha256 = "ba63dffc4410ee89193e1cb5a41989991bd77c61068da17e3156d136b7b0b3d8";

    /// <summary>Uses an existing compiler first; otherwise caches and verifies the pinned portable archive.</summary>
    public async Task<string> ResolveAsync(string cacheDirectory, IEnumerable<string> installedCandidates,
        CancellationToken cancellationToken = default)
    {
        foreach (var candidate in installedCandidates)
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);

        Directory.CreateDirectory(cacheDirectory);
        var archive = Path.Combine(cacheDirectory, $"nsis-{Version}.zip");
        if (!File.Exists(archive))
        {
            var temporary = archive + $".{Guid.NewGuid():N}.download";
            try
            {
                Console.WriteLine($"NSIS was not found. Downloading portable NSIS {Version} to {cacheDirectory}...");
                using var response = await client.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                await using (var output = new FileStream(temporary, FileMode.CreateNew))
                    await response.Content.CopyToAsync(output, cancellationToken);
                VerifyArchive(temporary);
                File.Move(temporary, archive);
            }
            catch (HttpRequestException exception)
            {
                throw new IOException("Cannot download portable NSIS. Connect once and retry, or install NSIS 3 " +
                    "from https://nsis.sourceforge.io/Download (standard install locations and PATH are supported).", exception);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw new IOException("Portable NSIS download timed out. Retry with a stable connection, or install NSIS 3 " +
                    "from https://nsis.sourceforge.io/Download (standard install locations and PATH are supported).", exception);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        // Verify cached archives as well, so offline provisioning cannot bypass the pinned checksum.
        VerifyArchive(archive);
        var compilerDirectory = Path.Combine(cacheDirectory, $"nsis-{Version}");
        var compiler = FindCompiler(compilerDirectory);
        if (compiler is not null) return compiler;
        if (Directory.Exists(compilerDirectory))
            throw new IOException($"Portable NSIS cache is incomplete: {compilerDirectory}. Remove that directory and retry.");

        var staging = Path.Combine(cacheDirectory, $"nsis-unpack-{Guid.NewGuid():N}");
        try
        {
            ZipFile.ExtractToDirectory(archive, staging);
            var extracted = Path.Combine(staging, $"nsis-{Version}");
            if (FindCompiler(extracted) is null)
                throw new IOException("The verified NSIS archive does not contain makensis.exe.");
            Directory.Move(extracted, compilerDirectory);
            return FindCompiler(compilerDirectory)!;
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
    }

    /// <summary>Returns PATH, explicit NSISDIR and standard per-machine/per-user installation candidates.</summary>
    public static IEnumerable<string> InstalledCandidates()
    {
        var directories = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Concat([Environment.GetEnvironmentVariable("NSISDIR") ?? "",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NSIS"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "NSIS"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "NSIS")]);
        foreach (var directory in directories.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var unquoted = directory.Trim().Trim('"');
            yield return Path.Combine(unquoted, "makensis.exe");
            yield return Path.Combine(unquoted, "Bin", "makensis.exe");
        }
    }

    private static string? FindCompiler(string directory) =>
        new[] { Path.Combine(directory, "Bin", "makensis.exe"), Path.Combine(directory, "makensis.exe") }
            .FirstOrDefault(File.Exists);

    private static void VerifyArchive(string path)
    {
        using var input = File.OpenRead(path);
        if (!Convert.ToHexString(SHA256.HashData(input)).Equals(ArchiveSha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException($"NSIS archive checksum mismatch: {path}. No compiler was executed. Remove the archive and retry.");
    }
}
