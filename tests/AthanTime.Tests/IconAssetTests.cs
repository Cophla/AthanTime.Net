using System.Xml.Linq;
using SkiaSharp;

namespace AthanTime.Tests;

/// <summary>Checks the editable vector source and desktop icon format/transparency contracts.</summary>
public sealed class IconAssetTests
{
    [Theory]
    [InlineData("athan-icon.svg", "0 0 910 910")]
    [InlineData("athan-icon-small.svg", "0 0 32 32")]
    public void SvgSources_AreSelfContainedVectorArtwork(string name, string viewport)
    {
        var svg = XDocument.Load(Asset(name)).Root!;
        XNamespace ns = "http://www.w3.org/2000/svg";
        Assert.Equal(ns + "svg", svg.Name);
        Assert.Equal(viewport, (string?)svg.Attribute("viewBox"));
        Assert.All(svg.Descendants(), element => Assert.Contains(element.Name, new[] { ns + "title", ns + "path" }));
        var path = Assert.Single(svg.Elements(ns + "path"));
        Assert.Equal("#79D8B4", (string?)path.Attribute("fill"));
        Assert.Equal("#123D30", (string?)path.Attribute("stroke"));
        Assert.Equal("evenodd", (string?)path.Attribute("fill-rule"));
        Assert.All(svg.DescendantsAndSelf().Attributes(), attribute =>
            Assert.DoesNotContain(attribute.Name.LocalName, new[] { "href", "style" }));
    }

    [Fact]
    public void Png_PreservesMintArtworkAndTransparentOpenings()
    {
        using var bitmap = SKBitmap.Decode(Asset("athan-icon.png"));
        Assert.Equal(910, bitmap.Width);
        Assert.Equal(910, bitmap.Height);
        Assert.Equal(new SKColor(0x79, 0xD8, 0xB4), bitmap.GetPixel(90, 470));
        // Background, crescent interior, balcony and both arched windows stay open.
        foreach (var (x, y) in new[] { (0, 0), (909, 909), (350, 400), (500, 390), (470, 520), (525, 520) })
            Assert.Equal((byte)0, bitmap.GetPixel(x, y).Alpha);
    }

    [Fact]
    public void Ico_ContainsDecodableTransparentFramesForDesktopSizes()
    {
        byte[] bytes = File.ReadAllBytes(Asset("athan-icon.ico"));
        using var reader = new BinaryReader(new MemoryStream(bytes));
        Assert.Equal(0, reader.ReadUInt16());
        Assert.Equal(1, reader.ReadUInt16());
        int[] sizes = [16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 96, 128, 256];
        Assert.Equal(sizes.Length, reader.ReadUInt16());
        foreach (int size in sizes)
        {
            int width = reader.ReadByte();
            int height = reader.ReadByte();
            Assert.Equal(size, width == 0 ? 256 : width);
            Assert.Equal(size, height == 0 ? 256 : height);
            Assert.Equal(0, reader.ReadByte());
            Assert.Equal(0, reader.ReadByte());
            Assert.Equal(1, reader.ReadUInt16());
            Assert.Equal(32, reader.ReadUInt16());
            int length = reader.ReadInt32();
            int offset = reader.ReadInt32();
            Assert.InRange(offset, 6 + 16 * sizes.Length, bytes.Length - length);
            using var frame = SKBitmap.Decode(bytes.AsSpan(offset, length).ToArray());
            Assert.NotNull(frame);
            Assert.Equal(size, frame.Width);
            Assert.Equal(size, frame.Height);
            Assert.Equal((byte)0, frame.GetPixel(0, 0).Alpha);
            Assert.Equal((byte)0, frame.GetPixel(size - 1, size - 1).Alpha);
            Assert.Contains(frame.Pixels, pixel => pixel.Alpha > 0);
            if (size <= 64) AssertSolidMinaret(frame);
        }
    }

    [Fact]
    public void NonWindowsDesktopPng_IsCompactWithASolidMinaret()
    {
        using var bitmap = SKBitmap.Decode(Asset("athan-icon-desktop.png"));
        Assert.Equal(64, bitmap.Width);
        Assert.Equal(64, bitmap.Height);
        Assert.Equal((byte)0, bitmap.GetPixel(0, 0).Alpha);
        AssertSolidMinaret(bitmap);
    }

    private static void AssertSolidMinaret(SKBitmap bitmap)
    {
        // Tiny cutouts must not break the shaft into scattered pixels at native desktop sizes.
        int x = 18 * bitmap.Width / 32;
        for (int y = 12 * bitmap.Height / 32; y < 26 * bitmap.Height / 32; y++)
            Assert.True(bitmap.GetPixel(x, y).Alpha >= 160, $"Broken minaret at {bitmap.Width}px, row {y}.");
    }

    private static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "ReferenceData", name);
}
