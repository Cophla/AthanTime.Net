using System.Text.RegularExpressions;

namespace InstallerBuilder;

/// <summary>The operating system on which the packaging command runs.</summary>
public enum BuildHost { Windows, Linux, MacOS }

/// <summary>A validated package request with native-host restrictions and Windows signing intent.</summary>
public sealed record InstallerPlan(string Format, string Runtime, string Version, BuildHost Host, bool SignWindowsBinaries = false)
{
    private static readonly string[] _runtimes =
        ["win-x64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"];

    /// <summary>Validates a request and chooses the host's default runtime when omitted.</summary>
    public static InstallerPlan Create(string format, string? runtime, string version, BuildHost host, bool windowsSigningRequested = false)
    {
        format = format.ToLowerInvariant();
        runtime = runtime?.ToLowerInvariant() ?? host switch
        {
            BuildHost.Windows => "win-x64",
            BuildHost.Linux => System.Runtime.InteropServices.RuntimeInformation.OSArchitecture ==
                System.Runtime.InteropServices.Architecture.Arm64 ? "linux-arm64" : "linux-x64",
            _ => System.Runtime.InteropServices.RuntimeInformation.OSArchitecture ==
                System.Runtime.InteropServices.Architecture.Arm64 ? "osx-arm64" : "osx-x64"
        };

        if (format is not ("native" or "portable"))
            throw new ArgumentException("Format must be native or portable.");
        if (!_runtimes.Contains(runtime, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Unsupported runtime: {runtime}.");
        if (!Regex.IsMatch(version, @"\A[0-9]+(?:\.[0-9]+){1,3}(?:-[A-Za-z0-9.-]+)?\z"))
            throw new ArgumentException("Version must be a numeric dotted version, optionally with a prerelease suffix.");
        if (format == "native" && host == BuildHost.MacOS && !Regex.IsMatch(version.Split('-')[0], @"\A[0-9]+\.[0-9]+\.[0-9]+\z"))
            throw new ArgumentException("Native macOS bundles require a three-part numeric base version (for example 1.2.3-beta.1).");
        if (format == "native" && !(host switch
            {
                BuildHost.Windows => runtime.StartsWith("win-", StringComparison.OrdinalIgnoreCase),
                BuildHost.Linux => runtime.StartsWith("linux-", StringComparison.OrdinalIgnoreCase),
                _ => runtime.StartsWith("osx-", StringComparison.OrdinalIgnoreCase)
            }))
            throw new ArgumentException("Native installers must be built on their target operating system. Use portable for a cross-OS ZIP.");

        bool signWindowsBinaries = windowsSigningRequested && runtime.StartsWith("win-", StringComparison.OrdinalIgnoreCase);
        if (signWindowsBinaries && host != BuildHost.Windows)
            throw new ArgumentException("Windows Authenticode signing requires a Windows build host.");
        return new InstallerPlan(format, runtime, version, host, signWindowsBinaries);
    }

    /// <summary>Names downloads by operating system and CPU rather than exposing runtime identifiers.</summary>
    public string PlatformName => Runtime switch
    {
        "win-x64" => "Windows-x64",
        "linux-x64" => "Linux-x64",
        "linux-arm64" => "Linux-arm64",
        "osx-x64" => "macOS-Intel-x64",
        "osx-arm64" => "macOS-AppleSilicon-arm64",
        _ => throw new InvalidOperationException($"Unsupported runtime: {Runtime}.")
    };

    /// <summary>Returns a GitHub-safe filename; installation identities stay independent of download names.</summary>
    public string FileName
    {
        get
        {
            string extension = Format == "portable" ? "-Portable.zip" : Host switch
            {
                BuildHost.Windows => "-Setup.exe",
                BuildHost.Linux => ".deb",
                _ => ".dmg"
            };
            return $"AthanTime-{Version}-{PlatformName}{extension}";
        }
    }
}
