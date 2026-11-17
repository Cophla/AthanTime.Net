using System.IO.Compression;
using System.Xml.Linq;

namespace InstallerBuilder;

/// <summary>Writes portable desktop layouts with Unix executable permissions, including when built on Windows.</summary>
public static class PortablePackage
{
    /// <summary>Archives a verified self-contained publish folder and the matching desktop launchers.</summary>
    public static void Create(string publishDirectory, string archivePath, InstallerPlan plan, string repository)
    {
        var mac = plan.Runtime.StartsWith("osx-", StringComparison.Ordinal);
        var linux = plan.Runtime.StartsWith("linux-", StringComparison.Ordinal);
        var prefix = mac ? "Athan Time.app/Contents/MacOS/" : "";
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            foreach (var file in Directory.EnumerateFiles(publishDirectory, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(publishDirectory, file).Replace('\\', '/');
                // Keep the user guide visible beside the macOS bundle, rather than inside it.
                var destination = mac && relative == "README.txt" ? relative : prefix + relative;
                Add(file, destination, (mac || linux) && relative == "AthanTime.Net");
            }
            if (mac)
            {
                var document = XDocument.Load(Path.Combine(repository, "packaging/macos/Info.plist.template"));
                var numeric = Version.Parse(plan.Version.Split('-')[0]);
                var text = document.ToString().Replace("@NUMERIC_VERSION@", $"{numeric.Major}.{numeric.Minor}.{Math.Max(0, numeric.Build)}")
                    .Replace("@VERSION@", plan.Version);
                var info = archive.CreateEntry("Athan Time.app/Contents/Info.plist");
                info.ExternalAttributes = 0x81a4 << 16;
                using (var writer = new StreamWriter(info.Open()))
                    writer.Write(text);
                Add(Path.Combine(repository, "packaging/macos/Launch-Athan-Time.command"), "Launch-Athan-Time.command", true);
                Add(Path.Combine(repository, "packaging/Remove-My-Settings.sh"), "Remove-My-Settings.command", true);
                Add(Path.Combine(repository, "packaging/macos/START-HERE.txt"), "START-HERE.txt", false);
            }
            if (linux)
            {
                Add(Path.Combine(repository, "AthanTime.Net/Assets/athan-icon.png"), "athan-time.png", false);
                Add(Path.Combine(repository, "packaging/Remove-My-Settings.sh"), "Remove-My-Settings.sh", true);
                Add(Path.Combine(repository, "packaging/linux/Launch-Athan-Time.sh"), "Launch-Athan-Time.sh", true);
                Add(Path.Combine(repository, "packaging/linux/Install-Shortcut.sh"), "Install-Shortcut.sh", true);
                Add(Path.Combine(repository, "packaging/linux/Install-Requirements.sh"), "Install-Requirements.sh", true);
                Add(Path.Combine(repository, "packaging/linux/START-HERE.txt"), "START-HERE.txt", false);
            }
            if (!mac && !linux)
                Add(Path.Combine(repository, "packaging/windows/Remove-My-Settings.cmd"), "Remove-My-Settings.cmd", false);

            void Add(string file, string name, bool executable)
            {
                var entry = archive.CreateEntryFromFile(file, name, CompressionLevel.Optimal);
                // ZIP's upper mode bits are portable; File.GetUnixFileMode cannot read Unix permissions on Windows.
                entry.ExternalAttributes = (executable ? 0x81ed : 0x81a4) << 16;
            }
        }
        if (mac || linux) MarkUnixArchive(archivePath);
    }

    private static void MarkUnixArchive(string path)
    {
        // .NET stamps the build host as the ZIP creator. Unix extractors need creator=Unix
        // as well as mode bits. Adjust only central-directory metadata in our own comment-free ZIP.
        using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite);
        using var reader = new BinaryReader(stream);
        stream.Seek(-22, SeekOrigin.End);
        if (reader.ReadUInt32() != 0x06054b50) throw new InvalidDataException("Unexpected ZIP end record.");
        stream.Seek(6, SeekOrigin.Current);
        var count = reader.ReadUInt16();
        stream.Seek(4, SeekOrigin.Current);
        var offset = reader.ReadUInt32();
        if (count == ushort.MaxValue || offset == uint.MaxValue)
            throw new InvalidDataException("Portable desktop archives require fewer than 65535 entries and a directory below 4 GiB.");
        stream.Position = offset;
        for (var index = 0; index < count; index++)
        {
            var header = stream.Position;
            if (reader.ReadUInt32() != 0x02014b50) throw new InvalidDataException("Unexpected ZIP directory entry.");
            stream.Position = header + 5;
            stream.WriteByte(3); // APPNOTE creator platform 3 = Unix.
            stream.Position = header + 28;
            var nameLength = reader.ReadUInt16();
            var extraLength = reader.ReadUInt16();
            var commentLength = reader.ReadUInt16();
            stream.Position = header + 46 + nameLength + extraLength + commentLength;
        }
    }
}
