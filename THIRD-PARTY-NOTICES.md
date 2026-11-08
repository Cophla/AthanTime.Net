# Third-party notices

All runtime and development dependencies are free to use; the project needs no paid service, API key, or proprietary runtime component.

- Avalonia UI packages: MIT license — <https://github.com/AvaloniaUI/Avalonia>
- SkiaSharp 3.119.4: MIT license with bundled native third-party notices — <https://github.com/mono/SkiaSharp>. Used by Avalonia and the development-only IconBuilder, which adds no SVG renderer dependency to the application.
- Inter through `Avalonia.Fonts.Inter`: SIL Open Font License 1.1.
- Adhan prayer-time library: MIT license — <https://github.com/stankovski/adhan-csharp>
- Noda Time 3.3.5: Apache-2.0 license — <https://github.com/nodatime/nodatime>. The license ships in `licenses/NODATIME-LICENSE.txt`. Its embedded IANA TZDB supplies offline civil-time and recurring seasonal rules on every OS; IANA time-zone data is public domain (<https://www.iana.org/time-zones>).
- SoundFlow 1.4.1: MIT license — <https://github.com/LSXPrime/SoundFlow>. Its NuGet package supplies native miniaudio (MIT/Unlicense); its LICENSE.md and SOUNDFLOW-THIRD-PARTY-NOTICES.txt are copied to `licenses/` in build and publish output. No optional FFmpeg or MIDI extensions are used.
- Microsoft.Extensions hosting, configuration, logging, and dependency injection: MIT license — <https://github.com/dotnet/runtime>
- Microsoft.Data.Sqlite 10.0.12: MIT license — <https://github.com/dotnet/efcore>. SQLitePCLRaw 2.1.12: Apache-2.0 — <https://github.com/ericsink/SQLitePCL.raw>. The default bundle supplies native SQLite for Windows, Linux and macOS; no system SQLite is needed. SQLite is public domain — <https://sqlite.org/copyright.html>.
- Serilog and its file/configuration/hosting integrations: Apache-2.0 license — <https://github.com/serilog/serilog>
- xUnit.net v3 and Microsoft Testing Platform integration: Apache-2.0/MIT licensed — <https://github.com/xunit/xunit>
- Xunit.DependencyInjection: MIT license — <https://github.com/pengweiqhca/Xunit.DependencyInjection>
- Optional Windows packaging uses open-source NSIS under the zlib/libpng license — <https://nsis.sourceforge.io/License>. Missing compilers use the pinned official NSIS 3.13 portable archive, verified with SHA-256 and retained with its licenses under ignored `artifacts/tools`; see [packaging provenance/offline setup](docs/PACKAGING.md#windows-setup). No compiler or downloader is shipped in the app.
- Offline place data derived from GeoNames: CC BY 4.0. Details are in `AthanTime.Net/Data/GEONAMES-LICENSE.txt`.

NVorbis 0.10.5 is MIT licensed (<https://github.com/NVorbis/NVorbis>); its license
is included under `licenses/`. It provides managed Ogg Vorbis decoding.

Bundled offline audio (Ogg Vorbis, each source recording CC0 1.0):

- `Assets/Audio/adhan.ogg`: [Beautiful adhan.ogg](https://commons.wikimedia.org/wiki/File:Beautiful_adhan.ogg), Adam-synagda, 2022.
- `Assets/Audio/takbeer.ogg`: [Allahuakbar.opus](https://commons.wikimedia.org/wiki/File:Allahuakbar.opus), Bod lnga klang, 2026.
- `Assets/Audio/alhamdulillah.ogg`: [Nl-alhamdulillah.ogg](https://commons.wikimedia.org/wiki/File:Nl-alhamdulillah.ogg), Marcel coenders, 2025; a short Dutch-speaker pronunciation of the Arabic phrase.

These files were converted from the previously bundled Commons MP3 transcodes.
The one-time MIT-licensed OggVorbisEncoder tool is not shipped with the app.

CC0 deed: <https://creativecommons.org/publicdomain/zero/1.0/>. Arabic Adhkar phrases are sourced in `Data/default-adhkar.json`; localized meanings are short project-authored paraphrases, not published scripture translations.
