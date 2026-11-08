using SkiaSharp;
using System.Globalization;
using System.Xml.Linq;

try
{
    bool check = false;
    string? output = null;
    for (int index = 0; index < args.Length; index++)
    {
        switch (args[index].ToLowerInvariant())
        {
            case "--help":
                Console.WriteLine("Usage: dotnet run --project tools/IconBuilder -c Release -- [--check] [--output DIRECTORY]");
                Console.WriteLine("Generates PNG/ICO from the master and small SVGs. --check verifies without writing; --output stages a preview.");
                return 0;
            case "--check": check = true; break;
            case "--output" when index + 1 < args.Length: output = args[++index]; break;
            default: throw new ArgumentException($"Unknown or incomplete option: {args[index]}");
        }
    }

    var repository = new DirectoryInfo(AppContext.BaseDirectory);
    while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "AthanTime.Net.slnx")))
        repository = repository.Parent;
    if (repository is null) throw new DirectoryNotFoundException("Run IconBuilder from a repository checkout.");

    string assets = Path.Combine(repository.FullName, "AthanTime.Net", "Assets");
    output = output is null ? assets : Path.GetFullPath(output);
    using var picture = LoadIcon(Path.Combine(assets, "athan-icon.svg"));
    using var smallPicture = LoadIcon(Path.Combine(assets, "athan-icon-small.svg"));

    // Exact 16/24px desktop sizes at common DPI scales avoid native resampling of thin features.
    int[] sizes = [16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 96, 128, 256];
    var frames = sizes.Select(size => Render(size <= 64 ? smallPicture : picture, size)).ToArray();
    using var iconStream = new MemoryStream();
    using (var writer = new BinaryWriter(iconStream, System.Text.Encoding.UTF8, leaveOpen: true))
    {
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int index = 0; index < sizes.Length; index++)
        {
            // ICO stores 256 as zero; each frame contains an independent transparent PNG.
            writer.Write((byte)(sizes[index] == 256 ? 0 : sizes[index]));
            writer.Write((byte)(sizes[index] == 256 ? 0 : sizes[index]));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(frames[index].Length);
            writer.Write(offset);
            offset += frames[index].Length;
        }
        foreach (var frame in frames) writer.Write(frame);
    }

    var outputs = new Dictionary<string, byte[]>
    {
        ["athan-icon.png"] = Render(picture, 910),
        ["athan-icon-desktop.png"] = Render(smallPicture, 64),
        ["athan-icon.ico"] = iconStream.ToArray()
    };
    bool current = true;
    foreach (var (name, bytes) in outputs)
    {
        string path = Path.Combine(output, name);
        bool matches = File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes);
        if (check)
        {
            Console.WriteLine($"{name}: {(matches ? "current" : "missing or stale; run IconBuilder without --check")}");
            current &= matches;
        }
        else
        {
            Directory.CreateDirectory(output);
            if (!matches) File.WriteAllBytes(path, bytes);
            Console.WriteLine($"{path}: {(matches ? "unchanged" : "generated")}");
        }
    }
    return current ? 0 : 1;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static byte[] Render(SKPicture picture, int size)
{
    using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul))
        ?? throw new InvalidOperationException("Could not create an icon render surface.");
    var canvas = surface.Canvas;
    canvas.Clear(SKColors.Transparent);
    var bounds = picture.CullRect;
    canvas.Scale(size / bounds.Width, size / bounds.Height);
    canvas.Translate(-bounds.Left, -bounds.Top);
    canvas.DrawPicture(picture);
    using var image = surface.Snapshot();
    using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
    return encoded.ToArray();
}

static SKPicture LoadIcon(string source)
{
    // Each icon source deliberately uses a flat, single-path SVG.
    // Reject unsupported styling rather than silently generating different artwork.
    var root = XDocument.Load(source).Root ?? throw new InvalidDataException("Missing SVG root.");
    XNamespace ns = "http://www.w3.org/2000/svg";
    var viewport = ((string?)root.Attribute("viewBox") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(value => float.Parse(value, CultureInfo.InvariantCulture)).ToArray();
    if (root.Name != ns + "svg" || viewport.Length != 4 || viewport[0] != 0 || viewport[1] != 0 ||
        !float.IsFinite(viewport[2]) || viewport[2] <= 0 || viewport[2] != viewport[3] ||
        root.Elements().Any(element => element.Name != ns + "path" && element.Name != ns + "title") ||
        root.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration &&
            attribute.Name.LocalName is not ("width" or "height" or "viewBox")))
        throw new InvalidDataException("Use a square SVG viewport at 0,0 with one flat path and an optional title.");
    var element = root.Elements(ns + "path").Single();
    string[] allowed = ["d", "fill", "stroke", "stroke-width", "stroke-linejoin", "fill-rule"];
    if (element.HasElements || element.Attributes().Any(attribute =>
            attribute.Name.Namespace != XNamespace.None || !allowed.Contains(attribute.Name.LocalName)) ||
        (string?)element.Attribute("fill-rule") != "evenodd" ||
        (string?)element.Attribute("stroke-linejoin") != "round")
        throw new InvalidDataException("Use explicit fill/stroke colors, evenodd fill and round joins; no CSS/transforms or linked resources.");

    using var path = SKPath.ParseSvgPathData((string?)element.Attribute("d")
        ?? throw new InvalidDataException("Missing SVG path geometry."))
        ?? throw new InvalidDataException("Invalid SVG path geometry.");
    path.FillType = SKPathFillType.EvenOdd;
    using var fill = new SKPaint
    {
        IsAntialias = true, Style = SKPaintStyle.Fill,
        Color = SKColor.Parse((string?)element.Attribute("fill") ?? throw new InvalidDataException("Missing fill color."))
    };
    using var stroke = new SKPaint
    {
        IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeJoin = SKStrokeJoin.Round,
        Color = SKColor.Parse((string?)element.Attribute("stroke") ?? throw new InvalidDataException("Missing stroke color.")),
        StrokeWidth = float.Parse((string?)element.Attribute("stroke-width") ?? "0", CultureInfo.InvariantCulture)
    };
    using var recorder = new SKPictureRecorder();
    var canvas = recorder.BeginRecording(new SKRect(0, 0, viewport[2], viewport[3]));
    canvas.DrawPath(path, fill);
    canvas.DrawPath(path, stroke);
    return recorder.EndRecording();
}
