# Application icon

`athan-icon.svg` is the editable vector master, with a 910 × 910 viewport. Its
single compound path preserves the crescent/minaret motif and transparent
balcony/window openings. It has no embedded bitmap, font or external resource.
The fill is mint green (`#79D8B4`) with an evergreen (`#123D30`) edge for definition
on light backgrounds; the light fill remains visible on dark backgrounds.

`athan-icon-small.svg` is the 32 × 32 desktop variant. Its solid minaret omits tiny
windows, the balcony cutout and finial that turn into fragmented pixels at 16 px.
It keeps the crescent/minaret motif, light green fill and transparent background.

`athan-icon.png` is the transparent 910 × 910 artwork export for Linux desktop
packaging. `athan-icon-desktop.png` is a 64 × 64 export of the small variant for
macOS/Linux window and tray icons. `athan-icon.ico` supplies Windows window, tray
and executable icons with PNG-encoded 32-bit frames at 16, 20, 24, 30, 32, 36, 40,
48, 60, 64, 72, 96, 128 and 256 pixels. Frames up to 64 px use the small source;
larger frames use the detailed master. Each size is rendered directly from SVG.

All app-owned native icons use `App.CreateDesktopIcon`: Windows receives the ICO
so Avalonia selects its native size/DPI frames. Loading the large artwork PNG
instead takes Windows' bitmap fallback and lets the OS resize it, which produced
rough, broken-looking tray/title-bar icons. Other desktops receive the compact
PNG. The main window, secondary windows and tray share this loading path.

## Regenerate

From the repository root with the .NET 10 SDK on Windows, macOS or Linux:

```text
dotnet run --project tools/IconBuilder -c Release
dotnet run --project tools/IconBuilder -c Release -- --check
```

Edit the appropriate SVG, regenerate, inspect small-size contrast and keep all
SVG/PNG/ICO variants together in local Git. `--check` regenerates in memory and
returns a failure if any tracked export is missing or differs, without writing.
To stage an export before replacing the tracked icons, append
`-- --output artifacts/icon-preview` instead.

The tool uses the project's SkiaSharp version and bundled Windows/macOS/Linux
native libraries. It supports square, single-path SVGs at origin 0,0 with explicit
fill/stroke colors, evenodd fill and round joins. It rejects CSS, transforms and
other element types instead of silently ignoring them. Keep editor output in
that simple path format. The tool needs no System.Drawing, Inkscape, separate SVG
renderer, image-generation service or platform-specific shell. Package restore
is a development prerequisite; subsequent generation works offline with restored
packages. Linux needs the same Skia system libraries as the desktop app. See
[third-party notices](../../THIRD-PARTY-NOTICES.md).

The application does not reference IconBuilder. Ordinary builds/publishes use the
tracked exports, with no rendering process or SVG parsing
at startup. Do not replace the WindowIcon/ApplicationIcon paths with raw SVG:
desktop/native icon consumers still need their supported raster formats.

## Provenance and verification

On 2026-10-23 the existing artwork was edited using built-in ImageGen to lighten
the green. It was subsequently redrawn as simple vector curves with a flat fill,
retaining the crescent, minaret, finial and transparent openings. The earlier
raster-edit prompt is retained below as provenance, not as a regeneration step.

The earlier raster update passed Debug/Release builds and all 240 tests in each
configuration. SVG migration verification is recorded in
[packaging notes](../../docs/PACKAGING.md#svg-icon-source-2026-10-23).
Native tray rendering, installers and Dock icons require matching-OS smoke tests.

## Previous raster edit prompt

Use case: precise-object-edit. Edit target: the supplied Athan Time transparent
crescent-and-minaret app icon. Lighten the existing artwork so it remains
recognizable as a tiny 16–32 pixel system tray icon against both dark and light
taskbar backgrounds. Keep the exact same crescent, minaret, little crescent finial,
proportions, orientation, placement, transparent openings and square canvas; do
not redesign or add any motif. Replace the deep green fill with a clear bright
mint/emerald green, target #79D8B4, matching the app's light green accent. Use a
clean nearly flat, fully opaque fill, not a translucent washed-out fill. Add only
a very thin crisp deep evergreen keyline, target #123D30, along the artwork's outer
edges and openings: enough to define the pale green on a light taskbar, but thin
enough that the bright green remains dominant when scaled to 16–32 px. This
keyline should read as roughly half a pixel at 24 px; do not add a thick black
cartoon outline or change the silhouette. No drop shadow, no glow, no badge, no
background rectangle/circle, no text, no gold/yellow pixels. Background and holes
must remain genuinely transparent. Preserve antialiased edges and original
canvas framing. Preserve original 910 by 910 pixel size if possible.
