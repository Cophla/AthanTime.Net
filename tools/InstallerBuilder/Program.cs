using System.Diagnostics;
using System.IO.Compression;
using System.Xml.Linq;
using InstallerBuilder;

try
{
    if (args.Any(argument => argument.Equals("--help", StringComparison.OrdinalIgnoreCase)))
    {
        Console.WriteLine("Usage: dotnet run --project tools/InstallerBuilder -c Release -- [--format native|portable] [--rid RID] [--version VERSION] [--dry-run]");
        Console.WriteLine("RIDs: win-x64, linux-x64, linux-arm64, osx-x64, osx-arm64. Native uses the current OS; portable works across OSes.");
        Console.WriteLine("Native macOS bundles use a three-part numeric base; prerelease labels remain visible. Windows signing uses the ATHAN_SIGNING_* environment settings.");
        return 0;
    }

    string format = "native";
    string? runtime = null;
    string? version = null;
    bool dryRun = false;
    for (int index = 0; index < args.Length; index++)
    {
        string option = args[index].ToLowerInvariant();
        if (option == "--dry-run") { dryRun = true; continue; }
        if (index + 1 >= args.Length) throw new ArgumentException($"Missing value for {args[index]}.");
        string value = args[++index];
        switch (option)
        {
            case "--format": format = value; break;
            case "--rid": runtime = value; break;
            case "--version": version = value; break;
            default: throw new ArgumentException($"Unknown option: {args[index - 1]}.");
        }
    }

    BuildHost host = OperatingSystem.IsWindows() ? BuildHost.Windows :
        OperatingSystem.IsMacOS() ? BuildHost.MacOS : BuildHost.Linux;
    string repository = FindRepository();
    // Read the canonical value, preserving labels instead of truncating to AssemblyVersion's numeric fields.
    version ??= XDocument.Load(Path.Combine(repository, "Directory.Build.props")).Descendants("Version").Single().Value;
    bool windowsSigningRequested = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ATHAN_SIGNING_CERTIFICATE_THUMBPRINT")) ||
        Environment.GetEnvironmentVariable("ATHAN_REQUIRE_SIGNING") == "1";
    InstallerPlan plan = InstallerPlan.Create(format, runtime, version, host, windowsSigningRequested);
    string output = Path.Combine(repository, "artifacts", "packages", plan.FileName);
    Console.WriteLine($"{plan.Format}: {plan.Runtime} -> {output}");
    if (dryRun) return 0;

    string signScript = Path.Combine(repository, "packaging", "windows", "Sign-Code.ps1");
    if (plan.SignWindowsBinaries)
    {
        int result = Run("powershell.exe", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", signScript, "-CheckOnly"], repository);
        if (result != 0) return result;
    }

    if (plan.Format == "portable")
    {
        string publishParent = Path.Combine(repository, "artifacts", "publish");
        Directory.CreateDirectory(publishParent);
        string publishDirectory = Path.Combine(publishParent, $"portable-{Guid.NewGuid():N}");
        Directory.CreateDirectory(publishDirectory);
        try
        {
            int result = Run("dotnet", ["publish", Path.Combine(repository, "AthanTime.Net", "AthanTime.Net.csproj"),
                "-c", "Release", "-r", plan.Runtime, "--self-contained", "true",
                $"-p:Version={plan.Version}", "-p:PublishSingleFile=false", "-p:DebugType=None",
                "-o", publishDirectory], repository);
            if (result != 0) return result;
            if (plan.SignWindowsBinaries)
            {
                // Publish can copy dependencies from NuGet; sign this final tree before archiving it.
                result = Run("powershell.exe", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", signScript, "-Path", publishDirectory], repository);
                if (result != 0) return result;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            string temporaryArchive = Path.Combine(Path.GetDirectoryName(output)!, Path.GetRandomFileName());
            try
            {
                ZipFile.CreateFromDirectory(publishDirectory, temporaryArchive);
                File.Move(temporaryArchive, output, true);
            }
            finally { if (File.Exists(temporaryArchive)) File.Delete(temporaryArchive); }
        }
        finally { Directory.Delete(publishDirectory, true); }
    }
    else
    {
        string? nsisCompiler = null;
        if (host == BuildHost.Windows)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            nsisCompiler = await new NsisCompiler(client).ResolveAsync(
                Path.Combine(repository, "artifacts", "tools"), NsisCompiler.InstalledCandidates());
            Console.WriteLine($"Using NSIS: {nsisCompiler}");
        }
        string script = host switch
        {
            BuildHost.Windows => Path.Combine(repository, "packaging", "windows", "build-installer.ps1"),
            BuildHost.Linux => Path.Combine(repository, "packaging", "linux", "build-deb.sh"),
            _ => Path.Combine(repository, "packaging", "macos", "build-app.sh")
        };
        string command = host == BuildHost.Windows ? "powershell.exe" : "bash";
        string[] arguments = host switch
        {
            BuildHost.Windows => ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script,
                "-Version", plan.Version, "-NsisCompiler", nsisCompiler!],
            BuildHost.Linux => [script, plan.Version, plan.Runtime == "linux-arm64" ? "arm64" : "amd64"],
            _ => [script, plan.Version, plan.Runtime]
        };
        int result = Run(command, arguments, repository);
        if (result != 0) return result;
    }

    if (!File.Exists(output)) throw new IOException($"Packaging finished without creating {output}.");
    Console.WriteLine($"Created {output}");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static string FindRepository()
{
    for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        if (File.Exists(Path.Combine(directory.FullName, "AthanTime.Net.slnx")) &&
            Directory.Exists(Path.Combine(directory.FullName, "packaging")))
            return directory.FullName;
    throw new DirectoryNotFoundException("Run InstallerBuilder from a build inside the Athan Time repository.");
}

static int Run(string command, IEnumerable<string> arguments, string workingDirectory)
{
    using Process process = new();
    process.StartInfo.FileName = command;
    process.StartInfo.WorkingDirectory = workingDirectory;
    process.StartInfo.UseShellExecute = false;
    foreach (string argument in arguments) process.StartInfo.ArgumentList.Add(argument);
    process.Start();
    process.WaitForExit();
    return process.ExitCode;
}
