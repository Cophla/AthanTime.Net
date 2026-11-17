# Third-party notices

All runtime and development dependencies are free to use; the project needs no paid service, API key, or proprietary runtime component.

- Avalonia UI packages: MIT license — <https://github.com/AvaloniaUI/Avalonia>
- SkiaSharp 3.119.4: MIT license with bundled native third-party notices — <https://github.com/mono/SkiaSharp>. Used by Avalonia and the development-only IconBuilder, which adds no SVG renderer dependency to the application.
- Inter through `Avalonia.Fonts.Inter`: SIL Open Font License 1.1.
- Adhan prayer-time library: MIT license — <https://github.com/stankovski/adhan-csharp>
- Noda Time 3.3.5: Apache-2.0 license — <https://github.com/nodatime/nodatime>. The license ships in `licenses/NODATIME-LICENSE.txt`. Its embedded IANA TZDB supplies offline civil-time and recurring seasonal rules on every OS; IANA time-zone data is public domain (<https://www.iana.org/time-zones>).
- SoundFlow 1.4.1: MIT license — <https://github.com/LSXPrime/SoundFlow>. Its NuGet package supplies native miniaudio (MIT/Unlicense); its LICENSE.md and SOUNDFLOW-THIRD-PARTY-NOTICES.txt are copied to `licenses/` in build and publish output. No optional FFmpeg or MIDI extensions are used.
- Microsoft.Extensions hosting, configuration, logging, and dependency injection: MIT license — <https://github.com/dotnet/runtime>
- Microsoft.Data.Sqlite 11.0.0: MIT license — <https://github.com/dotnet/efcore>. SQLitePCLRaw 3.0.5: Apache-2.0 — <https://github.com/ericsink/SQLitePCL.raw>. Its default bundle uses the public NuGet package SQLite 3.53.4 for native `e_sqlite3` on Windows, Linux and macOS; no system SQLite, paid build service or account is needed. SQLite is public domain — <https://sqlite.org/copyright.html>. The provider, wrapper and native-library notices ship in `licenses/SQLITE-NOTICES.txt`.
- Serilog and its file/configuration/hosting integrations: Apache-2.0 license — <https://github.com/serilog/serilog>
- xUnit.net v3 and Microsoft Testing Platform integration: Apache-2.0/MIT licensed — <https://github.com/xunit/xunit>
- Xunit.DependencyInjection: MIT license — <https://github.com/pengweiqhca/Xunit.DependencyInjection>
- Optional Windows packaging uses open-source NSIS under the zlib/libpng license — <https://nsis.sourceforge.io/License>. Missing compilers use the pinned official NSIS 3.13 portable archive, verified with SHA-256 and retained with its licenses under ignored `artifacts/tools`; see [packaging provenance/offline setup](docs/PACKAGING.md#windows-setup). No compiler or downloader is shipped in the app.
- Offline place data derived from GeoNames: CC BY 4.0. Details are in `AthanTime.Net/Data/GEONAMES-LICENSE.txt`.

NVorbis 0.10.5 is MIT licensed (<https://github.com/NVorbis/NVorbis>); its license
is included under `licenses/`. It provides managed Ogg Vorbis decoding.

Bundled offline audio (Ogg Vorbis):

- `Assets/Audio/adhan.ogg`: user-supplied `NasserAlqatamiAthan.ogg`, selected November 15, 2026. SHA-256 `34119FC6EA6ADA68E9ED20336C656533C27D097E1C4DF338E186C02F6EB952C7`.
- `Assets/Audio/takbeer.ogg`: user-supplied `NasserAlqatamiAllahoAkbar.ogg`, selected November 15, 2026. SHA-256 `D31FFDC368003681DA302A3EE8ABBF89A2C9351F38F2C9CB3EF6A6653AC35A2F`.
- Former bundled `alhamdulillah.ogg` (removed November 15, 2026): [Nl-alhamdulillah.ogg](https://commons.wikimedia.org/wiki/File:Nl-alhamdulillah.ogg), Marcel coenders, 2025, CC0 1.0; a short Dutch-speaker pronunciation of the Arabic phrase.

The supplied Athan and Takbeer files are copied byte-for-byte under stable bundled
filenames. Their upstream recording source and redistribution license have not
been supplied or independently verified. The former Commons CC0 attribution
applied to the former Alhamdulillah clip only. That removed clip was converted from the
previously bundled Commons MP3 transcode using the MIT-licensed OggVorbisEncoder
tool, which is not shipped with the app.

CC0 deed: <https://creativecommons.org/publicdomain/zero/1.0/>. Arabic Adhkar phrases are sourced in `Data/default-adhkar.json`; localized meanings are short project-authored paraphrases, not published scripture translations.
