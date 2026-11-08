# Packaging

All outputs include the .NET runtime, work offline, and are created under `artifacts/`. OS desktop/audio libraries must still be present. No paid
packaging service is required. Build native installers on their target OS so file
permissions and platform metadata are correct.
Every script publishes the same Release application; there is no separate Debug
edition for users. Users switch detailed diagnostic logging on or off in Settings
without reinstalling or restarting.
Source/IDE Debug runs allow concurrent development instances; packaged Release
builds retain the per-user single-instance guard regardless of logging or runtime
environment variables. See [running and verification](../README.md#run-and-verify).
The opt-in Start on login setting uses the installed executable path. Windows uses
the current-user Run key, Linux an XDG autostart desktop entry, and macOS a
current-user LaunchAgent. Moving a portable app after enabling startup leaves a
stale path; disable and re-enable the option after moving it. macOS can require
allowing the item in System Settings. Verify each target OS at login.
Minimized startup hides only the initial window opening. Tray click/Show restores
the same window without reapplying startup hiding; close/restore must work
repeatedly. Linux retains a taskbar route where no usable tray is available.

Audio is deployed through the SoundFlow NuGet package with native miniaudio binaries
selected by RID. Decoding requires NVorbis; all audio assets are `.ogg`.
Do not discard native libraries, enable trimming, or switch to single-file
publishing without re-testing. VLC/FFmpeg are not prerequisites.
See [audio deployment notes](AUDIO.md). Verify actual sound, file pickers, tray and
popup behavior on each OS; publishing a target from Windows is not an OS runtime test.
Use the [release checklist](RELEASE-CHECKLIST.md) for the repeatable native smoke
test matrix and offline TZDB/location/translation update procedure. Optional manual
public-repository CI builds/tests/packages all five RIDs with
`.github/workflows/desktop-validation.yml`; it never installs or launches a desktop
session and is disabled for private repositories to avoid billed runner minutes.
Local packaging remains independent of GitHub.

## Fresh repository preparation (2026-11-08)

The current release is **1.0.2-beta.6**. Source now versions
`Data/locations.db.zip` as an ordinary Git object (34,410,205 bytes), with the
archived JSON and regeneration tools. Shared `LocationCatalog.targets` uses
MSBuild's [Unzip](https://learn.microsoft.com/en-us/visualstudio/msbuild/unzip-task)
and [Touch](https://learn.microsoft.com/en-us/visualstudio/msbuild/touch-task)
tasks to extract SQLite incrementally under configuration/RID-specific `obj` and
ship `Data/locations.db`. This adds no runtime extraction, native dependency,
external unzip prerequisite or Git LFS requirement. App startup/search behavior,
data coverage and the five-RID matrix are retained.

The data builder generates SQLite and a single-entry ZIP with a fixed entry
timestamp from the same archived JSON. Raw builder SQLite is ignored. Tests
compare the archive/published database hashes, exercise the real shared MSBuild
target's first/incremental/changed/missing-output paths and reject missing,
corrupt or wrongly named archives. Existing catalog/Unicode/TZDB parity tests
remain required. Native audio/SQLite publish guards and per-OS setup/update/
uninstall behavior are unchanged.

Use [new repository instructions](GITHUB-READINESS.md) for the clean source
snapshot; do not copy the old `.git`, which retains the earlier oversized blob
and commit identity. Required source, licenses, media and all pending beta work
are included in the reviewed snapshot. Local profiles, private keys, logs,
internal refs and generated outputs are excluded. Fresh public commit identity
belongs in local Git configuration, not application defaults.

Validation on Windows 10.0.26300 x64, SDK 10.0.403: a fresh source-only repository
with no raw source database built Debug and Release with zero warnings/errors;
**425 tests passed in each**, none skipped. The separate data builder also built
both configurations without warnings/errors; the archived JSON regenerated the
catalog offline. Read-only IDE0051/IDE0052/IDE0005 checks, all five portable-plan
dry runs and generated-icon checks passed. The full initial Git diff exposed a
legacy trailing SQL-string space; expressing it as explicit `" WHERE "`
concatenation preserved search semantics and made the initial diff whitespace-clean.

The fresh repository contains 135 source/assets/docs files and one initial
commit using the chosen public project identity; no remote is configured. Its
135 current files and 135 historical blobs passed the local checked secret/home-
path patterns, public catalog/schema and audio-metadata review with no findings.
There are no raw `.db`, profiles, keys or generated outputs in the new history;
its largest blob is the 34,410,205-byte catalog ZIP. This is scoped inspection,
not an exhaustive guarantee against arbitrary secrets or personal identifiers.
The old repository/history stays local. Source archive instructions and the
required public-identity distinction are in [GitHub readiness](GITHUB-READINESS.md).

The rebuilt Windows installer is
`artifacts/packages/AthanTime-1.0.2-beta.6-win-x64-setup.exe`, **76,365,935 bytes**,
SHA-256 `2C0AD4F8509C3183F8587B7BA22E0AC0155020F581D451515E340DF5E82D7393`.
NSIS 3.13/.NET 10.0.14 and 7-Zip 26.05 archive/extraction checks passed, with
22 required files, five source/build hash comparisons and four x64 PE checks.
Neither compressed catalog input is shipped in the native payload, and publish
staging was cleaned. It was built from the original working copy with matching
application/build inputs; the initial new-repository build was validated before
the final documentation evidence was added. Raw evidence is retained locally in
ignored `artifacts/new-repo-20261108/`. The installer remains **NotSigned**;
neither installer nor native app was executed. macOS/Linux native execution,
Windows install/tray/audio and full-desktop resource measurements remain unverified.

## Deployment policy

The adopted direction is one shared C# application and one InstallerBuilder
workflow producing separate native OS packages, with portable archives available
as an alternative distribution format. Self-contained Release publishing remains
the default: the package includes .NET, so users do not need to install it before
first launch. Normal OS desktop/audio libraries are still prerequisites.

Keep the current SoundFlow/NVorbis audio adapter and ship the matching miniaudio,
SQLite and graphics assets for each supported RID. Native libraries implement
each OS's device interfaces while the application code stays shared. The current
matrix remains `win-x64`, `linux-x64`, `linux-arm64`, `osx-x64` and `osx-arm64`;
the architecture discussion did not add an experimental 32-bit release.

Smaller framework-dependent packages are future work after runtime prerequisite
detection, missing-runtime UX and offline setup have been designed and tested
for every target OS. A custom universal installer is also deferred. The current
installers do not download/install .NET at application startup.

The next release priorities are matching-OS install/update/uninstall and actual
audio/tray checks across the native matrix, plus trusted Windows signing and
Smart App Control verification. Use the [release checklist](RELEASE-CHECKLIST.md)
and [signing workflow](WINDOWS-SIGNING.md). A missing signing certificate or test
host remains a reported limitation; code/build success alone does not clear
those native release checks. No signing service, account or certificate purchase
is configured by adopting this policy.

## Search and settings fixes (2026-11-05)

The search/settings update shipped as **1.0.2-beta.5**. It addresses the two resource findings
from the beta.4 audit: one UI search worker retains only the latest pending
query/language, and unchanged/clamped-equivalent volume assignments skip saves
and notifications. Actual volume edits retain immediate persistence. A running
SQLite query is allowed to finish/dispose; obsolete output is discarded and
clear/disposal removes pending work. Failures do not block later requests.
The selected-date and live-tray display paths are now separate private methods
within the same view model, retaining shared scans on Today and one-second
reminder timing. See the [fix review](CODE-SIMPLICITY-REVIEW.md#beta5-fixes--2026-11-05).

The eight added regression cases cover these behavior/failure/lifecycle paths,
including reentrant result notifications and UI-thread updates. The controlled
five-unchanged-volume case now saves zero times; slow search cases keep at most
one active lookup. These are work/save-count checks, not whole-app RAM/CPU/GPU
measurements. No new dependency, native asset, caption, schema, default,
login/single-instance rule or installer/uninstaller behavior changed. The
Windows/macOS/Linux five-RID matrix remains unchanged. Matching-OS native
resource/integration checks and trusted Windows signing remain outstanding.

Final solution verification on Windows 10.0.26300 x64 with SDK 10.0.403: Debug
and Release builds passed with zero warnings/errors, and **420 tests passed in
each configuration**, none skipped. Read-only IDE0051/IDE0052/IDE0005 analysis
had no findings; 110 local documentation links/anchors, all five portable-plan
dry runs and Git diff whitespace checks passed. All eight language/theme and
existing UTC/seasonal/reminder/cache regressions are included in the suite.
Source hashes and the inventory (67 C# files, 539 declarations) are retained in
ignored `artifacts/search-settings-20261105/`; counts are review aids, not
resource/complexity thresholds. Previous beta.4 measurements and audit records
below remain historical; no whole-desktop performance gain is claimed here.

The rebuilt Windows installer is
`artifacts/packages/AthanTime-1.0.2-beta.5-win-x64-setup.exe`, **76,357,814 bytes**,
SHA-256 `889AB23A958CBB39C68BD7AC91DB6754FCD97B039C3C1F0AE74285331C822A31`.
NSIS 3.13 packaged the self-contained .NET 10.0.14 payload. 7-Zip 26.05
archive/extraction, 22 required-file checks, five source/build hash comparisons
and four x64 PE checks passed; beta.5 product metadata and numeric file version
1.0.2.0 match. Publish staging was cleaned. Evidence is in
`artifacts/search-settings-20261105/payload-result.json`. The installer remains
**NotSigned**; neither installer nor native app was executed on the host.

## Simplicity and resource validation (2026-11-05)

The pre-fix beta.4 source received a fresh
[simplicity/resource audit](CODE-SIMPLICITY-REVIEW.md#historical-beta4-validation--2026-11-05).
The review inventories all 67 handwritten C# files and records remaining
overlapping-search work, redundant volume saves and main-view-model maintenance
complexity. It does not certify whole-app RAM/CPU/GPU or native leak behavior.
Controlled headless probes observed two concurrent blocked searches and five
settings-save requests for five unchanged-volume assignments.

Both solution configurations and the separate LocationDataBuilder built without
warnings/errors on Windows 10.0.26300 x64, SDK 10.0.403. **412 tests passed in
each configuration**, none skipped. Read-only unused-code/import analysis,
PowerShell/XML/Bash syntax, icon output and five-RID portable-plan checks passed.
The first restricted build failed writing Avalonia's external build log; the
normal-cache-access rerun passed. Ignored evidence is in
`artifacts/validation-20261105/`. The earlier tick-loop figures below were not
remeasured in that audit. No production or packaging input changed during the
audit; its beta.4 installer/checksum below was not rebuilt for documentation.
The Windows/macOS/Linux native resource and signing limits remain unchanged.

## Idle resource reduction (2026-11-04)

This resource update shipped as **1.0.2-beta.4**. The clock/reminder timer still ticks once
per second. Ordinary ticks now notify bindings only when their clock, countdown,
progress, next-prayer or seasonal values change. Draft seasonal status is tracked
separately from committed minutes, including active-to-active minute changes.
Full language/theme/settings refreshes retain their existing notifications.

Selected-date and live-tray calculations scan the six cached events without
allocating filtered prayer arrays; Today reuses the same scan for both displays.
The prayer-row cache uses a small value key instead of formatting a string every
tick. Sunrise remains excluded from current/next-prayer calculations. Seasonal
baselines retain their bounded concurrent cache without a captured factory on
every hit. Custom seasons are constructed and validated once per evaluation,
retaining equal-date rejection, pre-change offsets, cross-year seasons and
start-inclusive/end-exclusive boundaries. No unbounded period cache, new timer,
package, forced GC or reduced reminder cadence is introduced.

### Measurement boundary and results

Before/after measurements used separate Release headless processes on Windows
10.0.26300 x64, SDK 10.0.403. The same real prayer calculator/TZDB/localization and
view model ran with an in-memory manual profile, fake audio/login, disabled
notifications/Adhkar and no window or SQLite lookup. The app timer was not started:
the injected clock advanced exactly one second per explicit Tick call. Each of
five samples per scenario warmed 2,000 ticks, then measured 10,000 ticks with
`GC.GetAllocatedBytesForCurrentThread`, Stopwatch elapsed time and property-event
counts. The manual coordinates were 33.56, 35.37; scenarios used UTC automatic or
Beirut custom annual rules, starting June 1, 2026 at 10:00 UTC.

The initial default-JIT samples varied as tiered optimization warmed. The
controlled comparison below set `DOTNET_TieredCompilation=0` only in benchmark
processes; installed-app/runtime defaults were not changed. Values are medians
of five samples, not unit-test thresholds:

| Scenario, 10,000 ticks | Allocated bytes before → after | Tick-loop elapsed ms before → after | Property events before → after |
| --- | --- | --- | --- |
| UTC automatic | 13,737,448 → 6,216,912 (54.7% less) | 30.3338 → 19.2820 (36.4% less) | 70,003 → 30,003 |
| Beirut custom | 161,906,560 → 54,860,128 (66.1% less) | 160.6692 → 89.7150 (44.2% less) | 70,003 → 30,003 |

These results demonstrate less work/allocation in the measured clock loop. They
are **not whole-process working-set, resident-memory, idle-CPU, GPU, startup or
battery measurements**; rendering, native devices, file logging and search were
outside the boundary. Actual desktop gains depend on bindings/rendering and the
OS. No native app/profile or security policy was changed. The benchmark project,
baseline assembly and raw/default/controlled samples are retained under ignored
`artifacts/resources-20261104/`. Its first temporary build emitted an unused
xUnit-generated entry-point warning; generation was disabled in that benchmark
project only, and its subsequent Release build had no warnings. Production/test
project entry-point configuration was unchanged.

Final verification: both beta.4 Debug and Release solution builds completed with
zero warnings/errors, and **412 tests passed in each configuration**, none skipped.
The six added cases cover changed-only tick notifications, prayer versus sunrise
boundaries, a separately selected date with a live tray, unapplied seasonal draft
transitions and active-to-active committed offset changes. Existing automatic,
half-hour/negative/cross-year seasonal rules, UTC reminder deduplication,
pause/resume, date rollover, cache and eight-language/theme checks also passed.
No timing/allocation threshold was added to unit tests. The final benchmark copy
and tested Release assembly hashes match in `build-evidence.json`.

The rebuilt Windows package is
`artifacts/packages/AthanTime-1.0.2-beta.4-win-x64-setup.exe`, **76,358,921 bytes**,
SHA-256 `07AF2CE215602E6075870882793A6700A35ABEB28B5222D60D1BA3CDDCE941BA`.
Artifact inspection completed November 5, 2026: 7-Zip 26.05 archive/extraction,
22 required-file checks, five source/build hash comparisons and four x64 PE
checks passed. Product metadata retains beta.4 and numeric file version 1.0.2.0;
the self-contained runtime is 10.0.14. Publish staging was cleaned. The package
remains **NotSigned**; neither installer nor native app was executed. Inspection
evidence is in `artifacts/resources-20261104/payload-result.json`. Portable-plan
dry runs also passed for all five RIDs without creating archives. The final
unused-member/import analyzer check (`IDE0051`, `IDE0052`, `IDE0005`) passed.

Windows, macOS and Linux source capability and the five target RIDs were reviewed;
no native dependency, runtime requirement, data/license, translation, profile,
login, single-instance, installer/uninstaller or deployment policy changed.
Actual desktop CPU/RAM/GPU and native install/audio/tray/login validation remain
unverified; the existing Windows Application Control/signing limitation remains.

## Repository-wide simplicity review (2026-11-04)

The [review](CODE-SIMPLICITY-REVIEW.md) inventories all 67 handwritten C# files
and reviews application/tool/test boundaries plus markup, scripts and CI. The
only production change separates activation dispatch from pipe I/O in
`SingleInstanceGuard`. Wire values, queue/lock/shutdown behavior, UI marshaling
and the supported matrix are retained. No product behavior, dependency, schema,
translation/default, asset or installer/uninstaller behavior changed; beta.3
remains the shared version. Larger view-model/search follow-ups are documented
instead of being hidden behind a blanket claim that all code is now simple.

Debug and Release builds on Windows 10.0.26300 x64 with SDK 10.0.403 passed with
zero warnings/errors. **All 406 tests passed in each configuration**, none
skipped. Existing process/UI regressions cover the extracted dispatch path.
Five PowerShell scripts and 12 XAML/project/build XML files parsed successfully.
The initial/final syntax inventories contain 518/519 method/constructor/local
function declarations, respectively; the listener's nested blocks decreased
from six to four. Counts are review aids, not test thresholds or quality scores.
Evidence lives in ignored `artifacts/simplicity-review-20261104/`.

The rebuilt Windows installer is
`artifacts/packages/AthanTime-1.0.2-beta.3-win-x64-setup.exe`, **76,353,442 bytes**,
SHA-256 `D0456224096675882EAC680B5990B92CBC743486A64EDC3D340F303F299ACB85`.
NSIS 3.13 and .NET 10.0.14 were retained. 7-Zip 26.05 archive/extraction checks,
22 required-file checks, five source/build hash comparisons and four x64 PE
checks passed; beta metadata is correct, no MP3/obsolete seed is published and
staging was cleaned. The preceding beta.3 output is preserved at
`artifacts/simplicity-review-20261104/before-review-beta3.exe`; the cleanup checksum
below is historical and no longer describes the current output path.

The setup remains `NotSigned`. No native installer or app was executed in this
review; the earlier Windows Application Control restriction remains unresolved.
Native install/update/audio/tray/login/focus checks and macOS/Linux native
execution/packaging remain unverified across the unchanged five-RID matrix.

## Source and test cleanup (2026-11-04)

The current release is **1.0.2-beta.3**. `AudioPlayer.cs` now groups the output
adapter, streaming Vorbis provider and bundled-recording paths. Preset tests live
beside calculator regressions, and the test project's SDK `Using` item replaces
the one-line global-usings file. These moves retain the same public types and
DI registrations. Distinct schedulers, UI editors and the `LocationSearch.cs`
source linked by the offline builder remain separate.

The obsolete 8 KB `Data/locations.json` seed has no code, project, builder or
recipe references and was neither embedded nor published by the current project;
it was removed.
The prebuilt SQLite catalog, archived `locations.json.br`, regeneration tools,
licenses and provenance remain intact. An unused `HijriLabel` property and a
duplicate caption-completeness test were also removed. Interface callbacks,
JSON metadata and bound properties were retained; the initial read-only
IDE0051/IDE0052/IDE0005 analysis found no private-member/import candidates.

All effective UI captions now live in `Data/ui-text.json`, replacing the inline
C# catalog and two-stage lookup. Before migration, the built service exported
all **1,688 effective values (211 captions in each of eight languages)**; each
was compared exactly with the consolidated JSON. No caption was translated or
rewritten. Tests now require exact language/key parity, actual lookup values,
case-insensitive identities, English fallback and visible unknown keys.

New decoder tests exposed NVorbis failing at an exact end-of-recording seek.
The adapter now represents that exclusive EOF without seeking the decoder;
reading reports completion once and a later seek restarts playback. It retains
streamed decoding and adds no full-recording buffers. Additional device-free
checks cover empty reads, invalid offsets, disposal and bundled identifiers;
missing-catalog coverage requires a visible failure with no database creation or
seed fallback. The first expanded Debug run passed 400/402 cases: one new parity
assertion exposed the split catalogs, and the EOF test exposed the seek error.
Both were addressed before final verification.

The package version advances from beta.2 to beta.3 to distinguish the decoder fix
and consolidated source. Existing profile/install/login/single-instance identities
and the five-RID matrix remain unchanged. Publishing stays self-contained and
folder-based, with no trimming or single-file switch. No dependency, native
library, startup preference or installer/uninstaller behavior changes are introduced.

Final Debug and Release builds on Windows 10.0.26300 x64 with SDK 10.0.403 passed
with zero warnings/errors. **All 406 tests passed in each configuration**, none
skipped; the removed duplicate and the added failure/boundary/fallback cases
account for the change from the preceding 396-test suite. The final read-only
IDE0051/IDE0052/IDE0005 check also completed without findings. Code/XAML/project
reference review, exact caption comparison, touched-source line endings and
`git diff --check` passed. No tracked source is hidden by Git exclusions.
Build/source hashes and migration evidence are under ignored
`artifacts/cleanup-20261104/`, based on commit `18f8777` plus the pending changes.
The pre-existing beta, window-position and activation work was preserved.
Portable-plan dry runs for all five RIDs also resolved beta.3 filenames correctly;
these created no archives and do not establish execution on another OS.

NSIS 3.13 rebuilt the Windows self-contained installer at
`artifacts/packages/AthanTime-1.0.2-beta.3-win-x64-setup.exe`, **76,363,171 bytes**,
SHA-256 `4910B7F0576EEEC4670381F8D180965341ABACF7ADBB3613B4D37416A5CB2375`.
7-Zip 26.05 archive verification/extraction passed, with 22 required-file checks,
five source/build hash comparisons and four x64 PE checks. The final DLL matches
the published Release build; SQLite and all three Ogg recordings match source.
The package has beta.3 product metadata, numeric file version `1.0.2.0` and the
.NET 10.0.14 runtime. No MP3 or obsolete seed is published; staging was cleaned.

No native app or installer was executed in this cleanup. The setup remains
`NotSigned`, and the earlier Windows Application Control restriction remains
unresolved. Windows native install/update, actual audio/tray/focus/login checks,
macOS/Linux execution and their native installers remain unverified. Matching-OS
smoke tests are still required; the five-RID source/plan review does not certify
those desktop integrations. Previous beta artifacts/evidence were retained.

## Simplicity and repository review (2026-11-04)

This earlier beta.2 source retained the direct mutex/pipe implementation: the mutex
prevents duplicate Release hosts and the same-user pipe requests the existing
window. These responsibilities require separate lifetime handling. Named protocol
constants now explain the wire values without changing the protocol or adding
services, packages, timers or runtime requirements. Desktop/window comments and
the canonical [engineering guide](../AGENTS.md) reflect repeat-launch behavior;
a simplicity review is part of its change completion rule. Existing beta and
window-position changes remain included.

Repository rules now exclude local .NET performance traces/dumps (`.nettrace`,
`.netperf`, `.gcdump`) alongside existing generated/private files. `.editorconfig`
and `.gitattributes` agree on UTF-8 text, LF shell/macOS launchers and CRLF
C#/Windows scripts. Source, tests, SQLite data, audio, artwork and licenses remain
visible to Git; no local IDE profile, remote or Git commit is created by this review.

Verification on Windows 10.0.26300 x64 with SDK 10.0.403: Debug and Release builds
passed with zero warnings/errors, and **396 tests passed in each configuration**,
none skipped. The added isolated-process regression rejects/reports a throwing
activation callback, then verifies another request succeeds and the lease stays
held. The first restricted build could not write Avalonia's external build cache;
the normal-cache build and both full suites passed. Git checks confirmed 21
generated/private examples are ignored, 16 source/asset examples are visible,
and no tracked file matches an ignore rule. Local documentation links, touched
C# UTF-8/CRLF and launcher executable modes were also checked. Evidence is under
ignored `artifacts/review-20261104/`; earlier test counts/checksums below are
historical, not results of this review.

The reviewed source was repackaged with NSIS 3.13 into
`artifacts/packages/AthanTime-1.0.2-beta.2-win-x64-setup.exe`: **76,353,155 bytes**,
SHA-256 `E5B1E1DAA55FDFD7A2C4A147513D301D1D7D1217FB3556197403F2C9E09679EE`.
7-Zip 26.05 archive verification/extraction passed, as did 22 required-file,
five source/build hash and four x64 PE checks. The package retains .NET 10.0.14,
beta.2 product metadata, SQLite/Ogg/NVorbis/miniaudio assets and no MP3; publish
staging was cleaned. The previous beta.2 artifact is retained under
`artifacts/review-20261104/before-review-beta2.exe`, so its historical checksum
below is not the checksum of the current output path.

No installer or native desktop integration was executed in this review. The
installer remains `NotSigned`; the earlier Application Control block is still
unresolved. Native Windows install/update/focus checks and macOS/Linux desktop,
audio and login checks remain unverified across the declared five-RID matrix.
No dependency, translation/default, installer/uninstaller behavior or platform
support changed in this cleanup.

## Repeat-launch activation (2026-11-04)

This beta.2 update added repeat-launch activation. A second Release launch restores and
activates the running main window, including tray/minimized startup, then exits.
It does not create another host, profile, reminder loop or audio service. Debug
builds continue to open independent instances and do not acquire the production
lease or start an activation pipe. Native macOS Finder/Dock `Reopen` events use
the same window path when Launch Services reuses the process; ordinary background,
file and URI activation do not reopen the window.

`SingleInstanceGuard` starts a same-user named-pipe listener after acquiring the
existing cross-session mutex and before host startup. A fixed-size protocol passes
the primary PID and a show command; there is no argument/file/URI forwarding or
runtime network connection. Requests arriving before the first window opening
are coalesced and delivered after its startup-minimized handlers. App marshals
restoration to the UI dispatcher, tries to leave native background mode, then
uses the existing tray/position-aware restore path. The listener stops at desktop
exit; its externally supplied DI instance keeps the mutex until the acquiring
thread disposes it after host shutdown.

Both pipe ends use `CurrentUserOnly`; on Windows this also checks elevation,
as documented by [Microsoft's pipe options](https://learn.microsoft.com/en-us/dotnet/api/system.io.pipes.pipeoptions?view=net-10.0).
The duplicate grants the primary PID foreground permission before sending its
request using [AllowSetForegroundWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-allowsetforegroundwindow).
Actual focus is subject to OS policy and the primary's desktop/session. Unix
uses a short per-application/user-hashed `/tmp` socket path to avoid macOS path
limits and inconsistent `TMPDIR` values; .NET owns socket creation/cleanup. See
[Avalonia's activation compatibility](https://docs.avaloniaui.net/docs/services/activatable-lifetime)
for the macOS-only shell reopen feature. All five target RIDs retain their
existing packages, native assets and runtime requirements.

Client handoff has a five-second deadline; connected peer I/O has a two-second
deadline, and connection-transition failures retry within the client deadline.
Requests carry an acknowledgement/receipt before disconnecting. Missing/older
primaries, mismatched elevation, denied IPC or shutdown never bypass the lease:
the duplicate returns status 1 instead of creating another host (status 0 means
the request was accepted, possibly queued). The primary logs channel/restore
failures through its configured logger. The running copy must include this
activation support; update/exit older copies before checking the new behavior.

Beta.2 distinguishes this update from beta.1 and sorts later in Debian's package
metadata (`1.0.2~beta.2`). Installation/profile identities and numeric macOS bundle
version `1.0.2` remain stable. The dated beta.1 artifact evidence below is historical.

Final verification on November 4 used Windows 10.0.26300 x64 and .NET SDK
10.0.403, based on commit `18f8777` with the pending beta, window-position and
activation changes. Debug and Release builds each completed with zero warnings
or errors; **395 tests passed in each configuration**, none skipped. The 16 new
cases cover repeated/concurrent process requests, startup coalescing, isolated
application identities, endpoint shutdown/restart, Debug policy, cancellation,
externally owned DI lifetime, and headless main-window restoration/shell reopen.
The final runs include Unix socket-error handling. These tests use isolated
identities/fake native integrations, not the production mutex or user profile.

The final unsigned Windows installer is
`artifacts/packages/AthanTime-1.0.2-beta.2-win-x64-setup.exe`, **76,355,030 bytes**,
SHA-256 `B502138D456201134F415B87B46C77EAC115E3891E0A8BC965FA4441A793AD2E`.
NSIS 3.13 built the self-contained .NET 10.0.14 payload. 7-Zip 26.05 archive
verification/extraction passed; 22 required payload files, five source/build
hash comparisons and four x64 PE architecture checks passed. The payload retains
the SQLite catalog/native library, Ogg/NVorbis and miniaudio assets, contains no
MP3, and reports product version `1.0.2-beta.2` (numeric file version `1.0.2.0`).
Generated publish staging was removed after packaging; these checks did not
install or uninstall the application on the host.

A disposable Windows Sandbox native attempt was blocked by Application Control
before the app window opened (Code Integrity events 3077/3033). Its blocked
apphost is byte-identical to the final package's apphost; the attempt preceded the
final managed Unix socket-error refinement. Consequently native foreground,
tray restoration and install/update behavior remain **unverified** for this
release. The Sandbox was closed; no security policy, trust store or host profile
was changed. Signing remains unavailable without a trusted certificate. macOS
Finder/Dock reopening, Linux desktop activation and native operation across the
other four RIDs also require matching-OS smoke tests; headless tests and shared
code do not certify them.

## Window position persistence (2026-11-04)

The main window now remembers its last normal desktop coordinates in per-user
`AthanTime/window-position.json`. `WindowPositionService` is registered in
`Program.CreateHost`; normal positions are tracked by the window and saved on
hide, close or deactivation. Drag/state notifications are coalesced in memory,
and unchanged positions cause no write. Saves use a same-directory temporary
rename; failures log through the app logger, keep the previous file/window usable
and remain retryable. Missing, incomplete or damaged JSON is not rewritten by
Load and retains centered first startup. No settings schema/default migration is
needed, and exported/imported preferences do not include desktop coordinates.

Restore, tray Show, monitor changes and DPI changes constrain the frame to an
available work area. Negative desktop coordinates are valid; a missing monitor
or gap falls back to the primary display. Oversized windows keep their title bar
reachable. Minimized/maximized/fullscreen coordinates do not overwrite the last
normal position, and minimized login startup does not rewrite it. Window size
and maximized-state persistence are outside this change. Application updates and
uninstall continue to preserve the separate per-user profile.

This uses existing Avalonia desktop APIs with no new packages or OS integrations;
all five RIDs retain their existing audio/SQLite/runtime payload. Absolute placement
depends on the window manager/compositor; Linux retains its X11/XWayland deployment
boundary. Native multi-monitor/DPI, macOS and Linux checks remain separate from
pure geometry and headless UI regressions. Existing captions/translations,
licenses, data, installer/uninstaller recipes and login preferences are unaffected.

This earlier window-position rebuild was **1.0.2-beta.1** and superseded the
November 3 artifact below. Its evidence is retained for history; the beta.2
artifact/checksum in the repeat-launch section above is current.

On Windows build 10.0.26300 x64 with SDK 10.0.403, both final builds passed with
zero warnings/errors and **all 379 tests passed in each of Debug and Release**,
none skipped. The 22 added cases cover coordinate round trips, missing/damaged
JSON, atomic-save failure/retry, negative coordinates, missing displays/gaps,
scaled physical frames, oversized windows, tray/relaunch restoration, unchanged
write suppression and minimized/maximized/fullscreen event ordering. Production
host tests verify that DI uses an isolated position file alongside the temporary
settings profile. The initial 36 affected lifecycle/storage/geometry cases also
passed. An intermediate NSIS build was deliberately stopped before adding the
DPI-change event hook; the final tests and rebuild use the completed code.

InstallerBuilder/portable NSIS 3.13 produced
`artifacts/packages/AthanTime-1.0.2-beta.1-win-x64-setup.exe` (76,361,514 bytes),
SHA-256 `b9b96319078574075bbe29b966fffb671793d0f92b611e25d2e7ca928b9f4bbe`.
The source is the working tree based on `18f8777`, including the pending beta and
position changes. 7-Zip 26.05 integrity/extraction checks passed: 20 required paths,
matching application/source database/three Ogg hashes, x64 apphost/coreclr/miniaudio/
SQLite PE headers and no MP3. The native-probe payload's DLL matches the final
package. Product name/version remain beta; numeric file version is 1.0.2.0 with
.NET 10.0.14 included. No device position file is shipped. Publish staging was
cleaned. The package remains `NotSigned`; the November 3 checksum below is historical.

Native position persistence was attempted in a fresh Windows Sandbox (guest
10.0.26100.0), with only a dedicated folder mapped and networking/clipboard
disabled. Application Control blocked `AthanTime.Net.exe` before any window
opened; Code Integrity events 3077/3033 confirm the signing-level failure. The
profile-preparation step ran, but native move/hide/restart assertions did not.
No security policy or trust store was changed, and the task-owned guest was
closed. Scripts/results/Code Integrity evidence are in ignored
`artifacts/window-position-20261104/`. Native Windows placement, real multi-monitor/
DPI transitions, macOS/Linux execution and their native installers remain
unverified. No app or installer was executed against the host profile. Current
documentation links, source whitespace and package checksum were checked.

## Beta release (2026-11-03)

This November 3 release used **1.0.2-beta.1**. Incrementing the numeric base from 1.0.1
lets existing installations upgrade to this beta instead of downgrading to a
prerelease of the version they already have. The main header/title uses a Beta
caption translated into all eight languages; About, support drafts, binary
product metadata, filenames and setup display names retain the semantic version.
UI version strings omit the SDK's appended source-revision metadata. Beta is a
release channel, independent of Debug/Release and diagnostic logging.

Windows setup and uninstall captions and installed-app DisplayName/DisplayVersion
include the full version. Installation directories, shortcuts, registry keys,
profile paths and the Release single-instance identity retain their existing names.
The Sandbox install/update procedure derives its expected version from the
versioned setup filename; the destination procedure requires an explicit setup.

Debian control metadata uses `1.0.2~beta.1`; filenames, the launcher label and
package description retain `1.0.2-beta.1`. The tilde orders it before final
`1.0.2`, and its higher base orders it after `1.0.1`, following
[Debian's version rules](https://www.debian.org/doc/debian-policy/ch-controlfields.html#version).
The package identity stays `athan-time` and deployment stays `/opt/athan-time`.

macOS `CFBundleVersion`/`CFBundleShortVersionString` use numeric `1.0.2`;
`CFBundleDisplayName`, `CFBundleGetInfoString`, the DMG volume and filename retain
the beta version. This keeps [Apple's numeric bundle-version format](https://developer.apple.com/documentation/bundleresources/information-property-list/cfbundleshortversionstring)
while leaving the channel visible. The bundle remains `Athan Time.app` with its
existing identifier and Applications-folder replacement flow. Multiple betas of
the same numeric base share numeric bundle metadata; there is no runtime updater
or channel migration. Match native OS/architecture checks to the unchanged five RIDs.

The dated sections below describe historical stable packages; their checksums
must not be mistaken for the beta output.

Verification on 2026-11-03 used Windows build 10.0.26300 x64, SDK 10.0.403 and
the working tree based on `18f8777` with these beta changes. Debug and Release
builds passed with zero warnings/errors, and **all 357 tests passed in each
configuration**, none skipped. Coverage includes rendered beta labels/window
titles and RTL bounds in eight languages/both themes, full semantic versions in
About/support/product metadata, native-plan filenames for five RIDs, and fake
Windows/Linux/macOS recipes. The first Debug suite passed 355/357: two new Linux
beta cases caught Bash expanding a replacement tilde into the home path. Explicit
literal-tilde construction fixed it; both complete final suites passed.

InstallerBuilder and portable NSIS 3.13 created
`artifacts/packages/AthanTime-1.0.2-beta.1-win-x64-setup.exe` (76,355,862 bytes),
SHA-256 `14c21a70a9a407d3b1f6e832570594c81ae6b861a3688e8a8fc374323a5e14a2`.
7-Zip 26.05 integrity/extraction checks passed for 20 required payload paths,
matching app DLL/source database/three Ogg hashes, x64 apphost/coreclr/miniaudio/
SQLite PE headers and no MP3. Product name is `Athan Time 1.0.2-beta.1`, numeric
file version is `1.0.2.0`, and .NET 10.0.14 is bundled. ProductVersion retains the
SDK source revision; the UI displays only `1.0.2-beta.1`. Fresh publish staging
was cleaned. XML/JSON, PowerShell/Bash syntax and documentation checks passed.

The beta remains `NotSigned`. Native testing was attempted in a fresh Windows
Sandbox (guest 10.0.26100.0), with networking/clipboard disabled and only a dedicated
test folder mapped. Application Control blocked the setup before the first
destination-caption check; Code Integrity events **3077/3033** identify the beta
setup/signing-level failure. No beta installation, actual upgrade, app launch or
uninstall checks ran, and no security policy/trust store was changed. The result,
Code Integrity evidence and the prepared 1.0.1-to-beta procedure are retained in
ignored `artifacts/beta-smoke-20261103/`. Its preceding 1.0.1 package matches the
historical `bc0cda...` checksum. Native Windows beta UI/update/removal checks remain
pending in an approved environment with appropriate signing; earlier native
successes below concern the old stable artifact. macOS/Linux native packages and
desktop checks require matching hosts and were not run. No installer was executed
against the host profile.

## Latest verification (2026-10-30)

This review is based on local commit `2354f60` plus the pending signing and
cross-platform packaging changes. The shared app/package descriptions and source
comments cover five prayers, the sunrise marker, per-user single-instance startup
and native packaging without promising trusted signatures. `AGENTS.md` remains
the canonical agent guide; signing procedures live in `WINDOWS-SIGNING.md`.

On Windows build 10.0.26300 x64 with SDK 10.0.403, Debug and Release builds
completed with zero warnings/errors and all **334 tests passed in each
configuration**, with no skipped cases. Coverage includes 26 cross-platform
packaging/plan cases added in this review. The first shell-fixture attempt failed
on Git Bash's Unix symlink behavior; the corrected fixtures fake link creation
along with native package tools, and the final complete suites passed. Earlier
Application Control failures below remain historical evidence; these passing
tests do not establish trust for an unsigned installer.

A deployment-policy follow-up retained the existing publish behavior and added
explicit self-contained, Release and multi-file argument assertions to the
Windows/Unix recipe tests. Both builds were rerun with zero warnings/errors;
all **17 targeted recipe cases passed in each configuration**, with none skipped.
The 334-case full-suite results above belong to the preceding review; this
follow-up reran the affected recipe cases only. Documentation links and diff
whitespace checks passed.

Direct Bash recipes now read the shared version and detect x64/ARM64, enforce
their native build host and validate version input before publishing. Native
macOS bundle versions require three numeric components, consistent with
[Apple's bundle-version format](https://developer.apple.com/documentation/BundleResources/Information-Property-List/CFBundleVersion).
The older portable PowerShell wrapper delegates omitted version/RID options to
InstallerBuilder. Windows portable ZIP payloads join native payloads in the
optional signing workflow; Linux/macOS RID outputs are excluded from Windows
Authenticode signing. A stale Windows uninstaller preserves shared registration
and shortcuts belonging to another installation.

The Windows installer/uninstaller script compiled successfully with portable
NSIS 3.13 and a small compile-only fixture. No fixture installer was executed.
Explicit MSBuild target checks with `ATHAN_REQUIRE_SIGNING=1` confirmed Linux
and macOS RID outputs skip Windows certificate access. PowerShell parsing and
Bash syntax checks passed. Shell regressions simulate Linux/Darwin and x64/ARM64
with fake publishing, package tools and links, including paths with spaces; they
do not install packages or certify native OS behavior. The disposable Windows
procedure now includes stale-uninstaller preservation checks, but has not run.

### Installer rebuild

The old Windows 1.0.0 and 1.0.1 setup outputs were removed, then the current
working tree was packaged through InstallerBuilder with SDK 10.0.403 and the
checksum-verified portable NSIS 3.13 on Windows build 10.0.26300 x64. The shared
version remains **1.0.1**. That rebuild produced
`artifacts/packages/AthanTime-1.0.1-win-x64-setup.exe` (76,336,511 bytes), SHA-256
`37cf0173e7dbf61e5c96970ebefcaf0f9fb8e4075b8a66700dbf7c7c9e3423fa`.
The older sizes/hashes below describe historical artifacts that were removed.
This artifact was subsequently replaced by the installation-scope build below.

The build completed successfully with self-contained .NET 10.0.14 and passed
RID-specific publish guards. 7-Zip 26.05 passed archive integrity checks;
extraction verified 20 required payload paths, including audio, location data,
runtime, SQLite and license files. The packaged application DLL matches the new
Release `win-x64` build; apphost version is 1.0.1.0. Source location/audio hashes
match, apphost/coreclr/miniaudio/SQLite PE headers are x64, and no MP3 files are
present. Build and verification staging were cleaned. No tests were rerun solely
for this artifact rebuild; the preceding build/test results remain dated evidence.

This rebuild includes the pending packaging changes but remains unsigned
(`NotSigned`); optional signing cannot establish publisher trust without a
provider certificate. Smart App Control and native install/update/uninstall
verification remain pending. No installer was executed on the user's profile.
Native macOS/Linux packages require matching hosts and were not rebuilt;
their desktop checks remain unverified.

### Windows installation scopes

The installation-scope build replaced the preceding rebuild at
`artifacts/packages/AthanTime-1.0.1-win-x64-setup.exe` (76,356,124 bytes), SHA-256
`c8b04d3cd004765693356255775dd017bc234f65e5eb61e73426081abd4fd44c`.
The development-startup rebuild below subsequently replaces this artifact.
InstallerBuilder completed with SDK 10.0.403, portable NSIS 3.13 and bundled
.NET 10.0.14 on Windows build 10.0.26300 x64. 7-Zip 26.05 passed archive
integrity checks; extraction confirmed 20 required payload paths and the three
NSIS plug-ins. The app DLL matches the Release `win-x64` build, source data/audio
hashes match, and plug-in hashes match the pinned compiler cache. Apphost,
coreclr, miniaudio and SQLite PE headers are x64; apphost version is 1.0.1.0.
There are no MP3 files. Build/verification staging was cleaned. Authenticode
reports `NotSigned`; obtaining trusted signatures remains separate work.

Windows setup now offers Only me and All users, preselecting an existing scope
when available. Shared installation uses Program Files, HKLM64 and the common
Start Menu. Only me retains the previous HKCU/user-folder behavior without
elevation. Existing paths, including legacy custom folders, stay within their
selected scope. Separate copies may coexist; directory scope markers reject
cross-scope overwrite and retain uninstaller ownership. All-users uninstall
leaves user profiles and login entries untouched. Linux/macOS deployment is
unchanged; see [installation procedures](#installation-and-updates).

On the same Windows/SDK/NSIS host, Debug and Release builds passed with zero
warnings/errors and all **337 tests passed in each configuration**, with none
skipped. After the final scope-page and UAC error-capture refinements, both builds
and the **nine affected Windows recipe/scope checks** passed again in each
configuration. These include three new source-contract checks for registry and
profile boundaries, elevation/result handling, and legacy/cross-scope ownership;
they inspect installer source and do not certify actual UAC or installation.
The final NSIS script also compiled with a minimal payload fixture, and the
Sandbox procedure passed PowerShell parsing. Documentation links and whitespace
checks passed. The fixture was not executed and was removed after checking.
An intermediate packaging attempt was deliberately stopped to correct Win32
LastError capture for UAC cancellation; the subsequent final build above passed.

The disposable native procedure now optionally checks both scopes with
`-TestAllUsers`, including HKLM/common shortcuts, coexistence and preservation
of user startup/settings/registration during machine removal. It was not run.
Interactive UAC approval/cancellation, alternate administrator credentials,
cross-user running-app locks and actual installation/update/uninstall remain
unverified; no live user installation or security policy was changed.

### Change completion review

The canonical [change completion rule](../AGENTS.md#change-completion-rule) now
requires reviewing affected tests, comments, descriptions, engineering/AI guidance
and supporting files on every change, including non-release work. README,
requirements and the release checklist link to that rule. Installer-tool metadata,
scope-preparation comments, test summaries and Sandbox help now describe the
current behavior and distinguish automated coverage from native certification.

The existing scope regression now also guards read-only preparation: it must not
request elevation or check executable write access before scope confirmation.
On Windows build 10.0.26300 x64 with SDK 10.0.403, both builds passed with zero
warnings/errors and all **nine affected Windows recipe/scope tests passed in
each configuration**, with none skipped. The full suite was not rerun in this
maintenance follow-up; earlier full-suite results above remain historical.
PowerShell parsing, project XML, local documentation links/anchors and diff
whitespace checks passed. Application/installer behavior is unchanged, and no
package was rebuilt: at that stage the installer SHA-256 still matched the scope
build above. Native checks and trusted signing remained unverified/pending.

### Concurrent development startup

The development-startup build produced
`artifacts/packages/AthanTime-1.0.1-win-x64-setup.exe` (76,342,062 bytes), SHA-256
`6c7819af7e4fbfda37a45bd591f7a898188aa7d6443406c63816cfcf48e852aa`.
The visible-destination rebuild below subsequently replaces this artifact.
InstallerBuilder completed with portable NSIS 3.13 and bundled .NET 10.0.14 on
the Windows/SDK host recorded below. 7-Zip 26.05 passed integrity checks, and
extraction verified 20 required app/data/audio/runtime/license paths. The app
DLL matches the updated Release `win-x64` build, source data/audio hashes match,
and apphost/coreclr/miniaudio/SQLite PE headers are x64. Apphost version remains
1.0.1.0; build and verification staging were cleaned. No installer was executed.
The package remains unsigned; trusted signing and native install/UAC verification
are still pending. macOS/Linux installers require matching hosts and were not
rebuilt. Earlier artifact hashes above are historical.

The working tree based on `74b353d` adds a build-time startup policy shared by
the application and isolated process probe. Debug builds skip mutex acquisition,
allowing concurrent development runs and coexistence with Release in either
launch order on Windows/macOS/Linux. Release still acquires the same stable
per-user, cross-session lease before host/UI startup and retains it through host
shutdown. Development environment variables and diagnostic logging do not
disable Release enforcement. This creates no separate shipped Debug edition.

Concurrent Debug instances share the existing user settings/logs and run separate
reminder/audio schedules; writes and dispatch are not coordinated. There is no
automatic profile isolation or new persisted option. See [development usage](../README.md#run-and-verify).

On Windows build 10.0.26300 x64 with SDK 10.0.403, both builds passed with zero
warnings/errors and all **340 tests passed in each configuration**, with none
skipped. Three added process cases cover parallel startup, coexistence with an
enforced lease in both launch orders, and Release enforcement while Development
environment variables are set. The probe uses isolated identities and no UI,
real profile, login entry or production mutex. Existing duplicate/race/restart/
abandoned-owner checks remain active in both configurations.

The first Release test invocation failed in the Microsoft.Testing.Platform CLI
handshake before returning results; it is a failed runner attempt, not a passing
suite. No leftover test process was found, and the sequential retry passed all
340 cases. Documentation links/anchors, project XML and diff whitespace checks
passed. macOS/Linux execution and native desktop/UAC checks remain unverified;
the startup exception introduces no new native integration or dependency.

### Visible installer destinations

The superseded visible-destination output was
`artifacts/packages/AthanTime-1.0.1-win-x64-setup.exe` (76,346,661 bytes), SHA-256
`d03b938bd84a817038101fbe15d18f093575d4269fce753d355840a75867f274`.
InstallerBuilder completed with SDK 10.0.403 and portable NSIS 3.13 on the host
recorded below. 7-Zip 26.05 passed archive integrity checks; extraction verified
20 required payload paths, a matching Release application DLL, matching source
data/audio hashes and x64 apphost/coreclr/miniaudio/SQLite PE headers. The package
includes .NET 10.0.14 and apphost version 1.0.1.0. Build and verification staging
were cleaned; Authenticode still reports `NotSigned`. No installer was executed.
Earlier package checksums above describe superseded outputs.
The user's subsequent blank-folder screenshot showed that this build still had
the missing-directory validation defect corrected below; compilation and source
checks alone had not established correct destination behavior.

Windows setup now keeps its destination page visible for both scopes and for
updates. Fresh installs show the selected scope's prefilled default, with editing
and Browse enabled. Updates display their registered folder read-only, keep it
selectable for copying, and disable Browse. Back/scope changes explicitly restore
editing for fresh installs. Leaving the page and entering installation both retain
the registered update path. Linux DEB's `/opt/athan-time` and macOS's Applications
deployment are unchanged; the [destination table](#default-install-locations)
documents all native defaults.

On Windows build 10.0.26300 x64 with SDK 10.0.403, both builds passed with zero
warnings/errors and all **21 affected Windows/Unix recipe cases passed in each
configuration**, with none skipped. A new source-contract case guards visible
destinations, editable fresh paths, read-only updates and Back/scope reset. It
does not execute an OS installer. Portable NSIS 3.13 also compiled the final
script with a minimal payload; that fixture was not run and was removed.
Local documentation links/anchors and diff whitespace checks passed.

The full application suite was not rerun for this packaging-only change; the
preceding 340-case results remain historical evidence. Actual Windows destination
page rendering, browsing, Back navigation and UAC flow still need the disposable
native checks in the [release checklist](RELEASE-CHECKLIST.md#manual-native-smoke-tests).
Native macOS/Linux packages were not rebuilt on this Windows host. Trusted signing
and native install/update/uninstall verification remain pending.

### Missing destination folder fix

The blank fresh-install field was traced to `ValidateTargetScope` normalizing
directly into `$INSTDIR` with NSIS `GetFullPathName`. Its final-component lookup
can fail for not-yet-created directories and clear the result. Validation now
passes a trailing directory separator, uses scratch output, checks errors/empty
results and rejects drive/share roots before assigning the selected path.
See [NSIS path-command behavior](https://nsis.sourceforge.io/Docs/Chapter4.html#getfullpathname).

Native `/D=` overrides are removed from NSIS `$CMDLINE`; parsing them there did
not preserve custom paths. The header default is now empty and `.onInit` captures
NSIS's initial `$INSTDIR` before scope defaults are assigned. Registered update
paths still take precedence. Linux/macOS recipes and application behavior are
unchanged.

On Windows host build 10.0.26300 x64 with SDK 10.0.403, Debug and Release builds
passed with zero warnings/errors and all **21 affected Windows/Unix recipe cases
passed in each configuration**, with none skipped. The strengthened regression
checks missing-directory normalization, scratch/error handling and native override
capture. The full application suite was not rerun for this packaging-only fix.

Unlike the preceding source-only checks, native destination controls were read
in Windows Sandbox (guest reported 10.0.26100.0) using
[`Test-InstallerDestination.ps1`](../packaging/windows/Test-InstallerDestination.ps1).
The old tiny compile fixture reproduced blank text/disabled Install for both
defaults and both absent custom `/D=` folders. The fixed fixture passed all five
cases: user/machine defaults, both custom paths with spaces, and a registered
update folder with read-only text/Browse disabled. Install was enabled in all
five corrected cases. No Install button was clicked. The initial probe reader
used `GetWindowText`, which cannot read another process's edit content; it was
corrected to bounded `WM_GETTEXT` and the old/fixed comparison reran successfully.
This is destination-control verification, not full install/update/uninstall,
UAC alternate-credential/cancellation or Smart App Control certification.
The final full installer, rebuilt with the login-startup fix below, also passed
all five destination cases on 2026-10-31 in Sandbox guest 10.0.26100.0. Machine
cases ran with an elevated guest token; none were skipped. Fresh-path checks
explicitly required absent directories, and no Install button was clicked.

### Login startup restoration (2026-10-31)

The startup-minimized `Opened` handler stayed attached for the window's lifetime.
Avalonia raises `Opened` again after Hide/Show, so tray restoration immediately
hid the window again. `MainWindow.PrepareStartupVisibility` now installs a
one-time handler that detaches before hiding; tray actions share
`MainWindow.ShowFromTray` to show, restore and activate the existing window.
Saved preferences and OS login registrations are unchanged. This shared Avalonia
path applies to Windows/macOS/Linux; Linux's minimized taskbar fallback remains.

On Windows build 10.0.26300 x64 with SDK 10.0.403, Debug and Release builds
passed with zero warnings/errors and **all 345 tests passed in each configuration**,
with none skipped. Four added headless cases cover minimized/normal startup,
tray availability, close-to-tray disabled and three subsequent restore cycles,
including unchanged preferences/no settings writes. Before the one-time detach
was added, the new minimized/tray regression failed at the first restore; the
other five exit/startup cases passed. The initial restricted build failed on
Avalonia's cache permissions; the normal-cache builds and final suites passed.

The native Windows probe uses a disposable Sandbox profile, disables reminders,
and launches the actual self-contained payload with `--start-minimized` and
separately with the saved preference. It posts Avalonia 12.1.3's tray callback
(`WM_USER + 1024`, tray identity 1, `WM_LBUTTONUP`) to the owned message window,
then checks real window visibility/minimized state after 1.4 seconds. The second
cycle posts twice. Close/restore repeats three times. This checks the native
callback path, not physical tray clicks, tray-menu navigation or a real sign-in.
The old payload reproduced hidden windows in all six checks. The final payload
passed all six with the same native main-window handle retained per launch:
visible and not minimized after every restore. Guest OS was 10.0.26100.0. An
initial probe posted before tray registration and failed; the probe now waits
for `Shell_NotifyIconGetRect` to confirm registration. A subsequent comparison
wrapper returned a child failure after its argument cases passed, without
capturing the guest exception; the standalone fresh-PowerShell retry passed all
six cases. No application changes were made between those native attempts.
Ignored probe scripts, old/final result JSON and failure records are retained
under `artifacts/destination-repro-20261030/`. Only the guest profile was used;
the probe terminated its own application processes and never changed login keys.
Matching-OS macOS/Linux tray/login checks remain unverified; no dependencies,
native architecture matrix, translations or installer/uninstaller ownership
behavior changed for this application fix.

The current output is `artifacts/packages/AthanTime-1.0.1-win-x64-setup.exe`
(76,370,432 bytes), SHA-256
`bc0cda270b5f0fc0523c0b2d546b6c0c1566c51816dbced47154e910c6b384f7`.
This supersedes the destination-only rebuild (76,342,939 bytes), which was
replaced before final-package native checks to include the tray fix.
InstallerBuilder/portable NSIS 3.13 completed on the Windows/SDK host above.
7-Zip 26.05 integrity/extraction checks passed: 20 required paths, matching app
DLL/source database/three Ogg hashes, x64 apphost/coreclr/miniaudio/SQLite PE
headers, no MP3, .NET 10.0.14 and apphost version 1.0.1.0. Build staging was
cleaned. The final package supplied both the destination-control checks and
the extracted native app used above. It remains `NotSigned`; full installation,
UAC/signing/Smart App Control and actual sign-out/login remain unverified.
Native macOS/Linux installers were not rebuilt on this Windows host. Local
documentation links/anchors, project XML, PowerShell parsing and diff whitespace
checks passed; no installer was executed against the host profile.

Local Git cleanup on 2026-10-31 added coverage-report, crash-dump, macOS debug-symbol
and editor-temporary-file ignore patterns. `git check-ignore --no-index` passed
26 generated/private and 18 required source/asset examples; no tracked paths are
hidden by the rules. README and the canonical agent guide describe the policy.
This ignore/documentation-only follow-up did not rerun application tests or
rebuild packages; the 345-case results above belong to the preceding app change.
The current installer's recorded checksum remains unchanged. Windows/macOS/Linux
path examples were checked locally; this adds no native-OS certification.

## Installer verification (2026-10-29)

Version **1.0.1** was published self-contained for Windows x64 using SDK 10.0.403,
runtime 10.0.14 and portable NSIS 3.13 on Windows build 10.0.26300 x64. The working
tree is based on `5d2936c`, with uncommitted changes. The output is
`artifacts/packages/AthanTime-1.0.1-win-x64-setup.exe` (76,325,676 bytes), SHA-256
`38c0fc48451ea77feb7647ec6aeaa31e22cf62cdc7a980545d2b5f79b051baff`.
7-Zip 26.05 passed archive integrity checks. The extracted application DLL hash
matches the Release `win-x64` build; the executable reports version 1.0.1.0.
RID-specific publish guards and payload inspection confirmed the bundled Ogg,
NVorbis, miniaudio, location database, managed/native SQLite and runtime files.
This installer includes the single-instance guard described below.

Windows setup now recovers the registered path (including legacy 1.0.0 custom
folders), retains it during updates, checks executable write access, disallows
skipping locked payload files and records `InstallLocation`. The recipe generates
an exact uninstall list from its fresh publish staging: uninstall preserves
user-added files and removes a login entry only if it matches that installation.
The default recipe version now comes directly from `Directory.Build.props`.

Both Debug and Release builds completed with zero warnings/errors. An initial
Debug run passed all 294 existing tests. After adding the recipe regression, the
latest full Debug run passed 290 of 295 tests: five single-instance cases could
not run their helper. A standalone helper invocation confirmed Application Control
blocked `SingleInstanceProbe.dll` with `0x800711C7`. The full Release attempt
passed 278 of 285 discovered cases, with seven failures loading blocked
`InstallerBuilder.dll`; nine expected plan cases were not reported. These are **failed
test runs**, not passing suites. The new Windows recipe regression passed in the
full Debug run and a focused Release run. It uses fake publishing/compiler
commands to verify shared version selection, payload-only uninstall entries,
dollar-sign escaping, child-before-parent empty-directory removal and staging
cleanup, without running an OS installer. PowerShell parse checks and Bash syntax
checks also passed.

Application Control also blocked the root .NET packaging helper on this host;
the installer was built with the existing PowerShell recipe and an explicit
verified NSIS path. No policy was changed. Windows Sandbox was launched for the
native [install/update procedure](#installation-and-updates), but produced no
test progress/results or targetable window; the task-owned launcher was stopped.
No native installation/update success is claimed. macOS/Linux native packages
were not rebuilt: this host has no macOS environment or installed WSL distribution.
The existing native smoke matrix remains unverified. This Windows package is
unsigned and must still pass the checks on an approved/disposable target host.

## Single-instance verification (2026-10-29)

The working tree based on `5d2936c`, on Windows build 10.0.26300 x64 with
SDK 10.0.403, passed Debug and Release builds with zero warnings/errors and
all 294 tests in each configuration. Five new process-level cases use the
production `SingleInstanceGuard` with isolated names: duplicate rejection and
restart after normal/forced exit, concurrent launches with one winner, independent
identities, and recovery of an abandoned mutex with an outstanding handle. The
test-only `tests/SingleInstanceProbe/` helper is built in the matching configuration;
it opens no windows and uses no real profile or startup registration.

`Program.cs` acquires the guard before starting the host or Avalonia and retains
it through shutdown. It uses .NET's [named mutex scope options](https://learn.microsoft.com/en-us/dotnet/api/system.threading.namedwaithandleoptions?view=net-10.0)
for current-user access across sessions; Windows additionally includes the user
SID in the name so different accounts have independent mutexes. Ownership and
release stay on the entry thread, including when Windows runs the UI on its STA
thread. An [abandoned mutex](https://learn.microsoft.com/en-us/dotnet/standard/threading/mutexes#abandoned-mutexes)
transfers ownership to the next launch. Product behavior is in
[desktop requirements](REQUIREMENTS.md#required-behavior).

The guard adds no runtime package, network listener, background polling or lock
file cleanup. That initial verification did not rebuild an installer; the 1.0.1
package recorded above now includes the guard. Actual desktop/manual/login launch checks,
multiple OS users/sessions and native macOS/Linux execution remain unverified.
Follow the [native smoke matrix](RELEASE-CHECKLIST.md#manual-native-smoke-tests).

## Installer verification (2026-10-25)

Root `Create-Installers.cmd`, `.command` and `.sh` launchers delegate to the
existing InstallerBuilder. They preserve the shared version/default RID, forwarded
options, and failure exit codes; interactive no-argument launches retain the
result. Executable permissions for the two Unix files are recorded in local Git.

On Windows build 10.0.26300 x64 with SDK 10.0.402, the working tree based on
`5d2936c` passed Debug and Release builds with zero warnings/errors and all
289 tests in each configuration. Three launcher regression cases exercise
default requests, explicit arguments and a failing fake packaging command from
an unrelated working directory, with spaces in the repository path. No OS
installer is invoked by those tests. Seven compiler-preparation cases cover
installed root/`Bin` layouts, invalid cached/downloaded archives, HTTP failures
and transport timeouts, using fake HTTP responses without network access.

The real Windows launcher passed `--dry-run` from outside the repository and
reported the expected versioned setup path; the missing-SDK branch returned 1
with a readable message. Both Unix scripts passed Bash syntax and real
`--dry-run` checks using Git Bash on Windows. These are Windows shell checks,
not native macOS/Linux execution. Windows packaging now detects installed NSIS
or prepares the pinned, checksum-verified portable compiler in the repository's
ignored build cache. Native launch/install smoke tests remain required on each
matching OS.

The real Windows launcher then completed `--version 1.0.0` using portable NSIS
3.13 and the self-contained .NET 10.0.13 runtime. It created
`artifacts/packages/AthanTime-1.0.0-win-x64-setup.exe` (76,352,261 bytes), SHA-256
`0665a0f81e0e3c5a5c00da583b815a2653a932c8703722e234284aa77d54cbf4`.
7-Zip 26.05 reported a successful archive integrity check; the payload contains
the application, uninstaller, Ogg audio, NVorbis, miniaudio, `Data/locations.db`,
managed SQLite and bundled `e_sqlite3.dll`. Publishing also passed the project's
RID-specific dependency guards. Initial NuGet connectivity stalled on this host;
the missing Microsoft runtime packages were provisioned from signature-verified
official downloads before the successful build. This is a completed Windows
installer build, not an installation/runtime smoke test. No installer was
installed; native macOS/Linux builds and desktop integration remain unverified
by this change.

## Documentation and reminder verification (2026-10-24)

The summary, package descriptions, canonical agent rules, help/release procedures
and source comments now consistently distinguish the five prayers from the
Shoroq/sunrise time marker. Icon guidance covers both SVG sources and the native
ICO/compact-PNG loading path. Existing dated validation/profiling entries below
remain historical evidence; this refresh claims no startup optimization.

On Windows build 10.0.26300 x64 with SDK 10.0.402, the working tree based on
`5d2936c` passed Debug and Release builds with zero warnings/errors and all
279 tests in each configuration. New language-specific regression cases guard
sunrise captions against prayer wording in all eight languages. The existing
midnight/sunrise audio test now also verifies that sunrise never becomes the
next prayer, keeps its displayed time, and still dispatches optional audio.
The follow-up adds 22 cases for sunrise switch combinations/timing, JSON settings
round trips, and restart suppression across before/at/after reminders for prayer
and sunrise events. Dispatch checks verify saving the UTC identity before the
popup event. Host tests also assert deferred window construction and reuse while
retaining the production service lifetime. These checks use isolated profiles,
fake audio and headless windows, with no machine-dependent timing thresholds.
Earlier checks on this working tree verified local documentation links/anchors
and the project XML; IconBuilder `--check` confirmed all tracked PNG/ICO exports
were current.

Calculation, scheduling and settings behavior are unchanged. Native installers,
desktop integration and macOS/Linux execution were not rerun; the
[native-validation limits](#compatibility-and-validation-limits) and release
smoke-test matrix still apply.

## One-click launchers

Use the launcher at the repository root for the computer building the installer:

| OS | Launcher | Native output | Prerequisites |
| --- | --- | --- | --- |
| Windows | Double-click [Create-Installers.cmd](../Create-Installers.cmd) | `*-win-x64-setup.exe` | .NET 10 SDK; NSIS is detected or downloaded automatically |
| macOS | Double-click [Create-Installers.command](../Create-Installers.command) | `*-osx-x64.dmg` or `*-osx-arm64.dmg` | .NET 10 SDK and built-in `hdiutil` |
| Debian/Ubuntu Linux | Run [Create-Installers.sh](../Create-Installers.sh) in a terminal | `*-amd64.deb` or `*-arm64.deb` | .NET 10 SDK, Bash and `dpkg-deb` |

All launchers use the existing InstallerBuilder, choose the current host's default
architecture, and write to `artifacts/packages`. The version comes from
`Directory.Build.props`; no version prompt or script edit is needed. Native
installers are built on their matching OS. They are created, never installed or
uploaded. Windows uses installed NSIS or caches a verified portable copy locally;
it does not change the system installation or `PATH`. The SDK and other OS tools
are not installed automatically. Initial tool download and package restore may
need network access when build dependencies are not already cached. Launchers
suppress .NET's first-run banner, telemetry and unrelated HTTPS development-
certificate setup for a focused packaging log.

Direct Linux/macOS recipes use the same shared version and detect the host CPU
when version/architecture is omitted. `packaging/common.sh` owns those Bash
defaults. Each native recipe rejects another build OS before invoking package
tools. macOS versions must be three numeric components; portable archive labels
can still use two/four components or a prerelease suffix.

On macOS/Linux, enable execution once if the checkout/download lost permissions:

```bash
chmod +x Create-Installers.command Create-Installers.sh
```

For Linux file managers that offer it, choose **Run in Terminal**. Otherwise run
`./Create-Installers.sh` from a terminal. No-argument interactive launches wait at
the end so the result/error stays visible. Arguments are forwarded unchanged to
InstallerBuilder and suppress that final wait:

```powershell
.\Create-Installers.cmd --dry-run
.\Create-Installers.cmd --version 1.2.3
```

```bash
./Create-Installers.sh --dry-run
./Create-Installers.command --version 1.2.3
```

`--dry-run` prints the planned package without publishing or invoking OS packaging
tools. Use `--help` for all options, including portable ZIPs and an explicit RID.
The launcher returns the packaging command's exit code, including failures.

## One .NET packaging command

Icon artwork lives in `AthanTime.Net/Assets/athan-icon.svg`. Keep the generated PNG
and ICO alongside it, including the small desktop SVG/PNG variant: Windows
windows/tray/executable use ICO, other desktop windows/tray use the compact PNG,
and Linux packaging uses the large PNG. [IconBuilder](../AthanTime.Net/Assets/ICON.md) regenerates them
on Windows, macOS or Linux before packaging; it is not an application dependency.

The .NET 10 project `tools/InstallerBuilder/` is the entry point for all formats.
It has no packaging NuGet dependencies. Run it from this repository with the
.NET 10 SDK. By default it creates the native package for the current OS:

```text
dotnet run --project tools/InstallerBuilder -c Release
```

Select the target runtime for a different architecture on the same OS, for
example `--rid linux-arm64` or `--rid osx-x64`. `--dry-run` shows the output
without publishing; `--help` lists the supported RIDs. Native output is an
NSIS `.exe` on Windows, `.deb` on Linux, or `.dmg` containing an `.app` on
macOS. The .NET command calls the existing recipes below; it does not require
PowerShell on Linux or macOS. Outputs are in `artifacts/packages/`.

The compiler/packaging tools are platform-specific, so there is no single
portable .NET command that produces fully native installers for every OS from
one machine. Use each target OS (or its CI runner) for its native installer.
This also permits an OS-specific installation smoke test. NSIS and `dpkg-deb`
are free/open-source tools. Apple's bundled `hdiutil` is free to use on macOS
but is not open-source; replacing it with a third-party DMG tool would add
complexity and would not remove macOS signing/notarization requirements.

## Portable archives

To make a ZIP for any supported RID from any host, use:

```text
dotnet run --project tools/InstallerBuilder -c Release -- --format portable --rid osx-arm64
```

This includes the .NET runtime but is not a native installer. ZIP extraction
may lose Unix executable permissions; use the native package for a polished
Linux/macOS installation. Publishing a different RID is not a runtime test.

The older PowerShell recipe remains available:

From PowerShell:

```powershell
./packaging/build-portable.ps1 -Runtime win-x64 -Version 1.0.1
```

Supported RIDs are `win-x64`, `linux-x64`, `linux-arm64`, `osx-x64`, and
`osx-arm64`.

## Windows setup

Run the root launcher, or the direct recipe below. Both share InstallerBuilder's
NSIS discovery and portable-tool preparation:

```powershell
./packaging/windows/build-installer.ps1
```

InstallerBuilder searches `PATH`, `NSISDIR`, both Program Files NSIS locations,
and `%LOCALAPPDATA%\Programs\NSIS`, supporting root and `Bin` compiler layouts.
When no compiler is installed, it downloads the pinned [official NSIS 3.13 portable
archive](https://sourceforge.net/projects/nsis/files/NSIS%203/3.13/nsis-3.13.zip/download)
to `artifacts/tools/nsis-3.13.zip`, verifies SHA-256 before extraction, and reuses
`artifacts/tools/nsis-3.13/Bin/makensis.exe` on later builds. The pinned SHA-256 is
`ba63dffc4410ee89193e1cb5a41989991bd77c61068da17e3156d136b7b0b3d8`;
the version/URL/checksum are owned by `tools/InstallerBuilder/NsisCompiler.cs`.
The complete archive, including NSIS licenses, stays in the ignored build cache;
no download/compiler is added to the application runtime.

For an offline build machine, install NSIS 3 in one of those locations or copy
the exact verified ZIP to `artifacts/tools/nsis-3.13.zip` before building. Cached
archives are rechecked; a mismatch stops the build before extraction/execution.
Failed downloads leave no partial archive. `--dry-run` does not prepare NSIS.

Windows setup offers **Only me** (default, no elevation) and **All users**
(administrator permission). Only me uses `%LOCALAPPDATA%\Programs\Athan Time`,
HKCU uninstall registration and the user's Start Menu. All users uses 64-bit
Program Files, HKLM's 64-bit registry view and the common Start Menu. Setup
starts without elevation and requests it only for machine operations, including
machine updates and removal. UAC cancellation leaves the selected operation
unperformed; silent callers receive the elevated child's exit code (1223 for
UAC cancellation, 2 for script-rejected operations).

Interactive Windows setup always shows a destination page. Fresh installations
prefill the selected scope's default and allow browsing; registered updates show
their existing folder read-only with Browse disabled. The retained path remains
selectable for copying. Back/scope changes restore editing for a fresh scope.
This replaces the earlier behavior that hid the directory page during updates.

### Default install locations

| Package / scope | Default destination |
| --- | --- |
| Windows Only me | `%LOCALAPPDATA%\Programs\Athan Time` |
| Windows All users | 64-bit Program Files, normally `C:\Program Files\Athan Time` |
| Linux DEB | `/opt/athan-time` (fixed by the package manager) |
| macOS DMG | Copy **Athan Time.app** into `/Applications` using the supplied link; retain the chosen folder for updates. |

Application settings, recordings and login-startup choices remain per-user in
either mode. Linux's DEB is system-wide; macOS uses the chosen Applications
folder. Installation scope does not change the shared application or its
per-user Release single-instance guard; the source-build Debug exception is
independent of install scope.

If Application Control blocks the packaging helper, the existing recipe also
accepts an installed/verified compiler directly:

```powershell
./packaging/windows/build-installer.ps1 -NsisCompiler ./artifacts/tools/nsis-3.13/Bin/makensis.exe
```

This publishes with the SDK and invokes NSIS directly; it does not execute the
blocked helper or change Windows policy. App/test execution can still be blocked
on that host; record the failure and use an approved build/test environment.

## Installation and updates

Exit Athan Time using **Exit**, including when it is hidden in the tray, before
replacing installed files. Keep the same installation path so login startup
continues to point to the application. Settings remain in the current user's
application-data `AthanTime` folder; installers do not import or reset them.

| OS | Fresh install or update |
| --- | --- |
| Windows x64 | Run `AthanTime-1.0.2-beta.6-win-x64-setup.exe`. Interactive setup offers Only me/All users and preselects the existing scope. Each selected installation retains its registered scope and original folder, including custom folders from 1.0.0. No prior uninstall is required. |
| Debian/Ubuntu | Build the matching `amd64`/`arm64` DEB on Linux, then use `sudo apt install ./AthanTime-1.0.2-beta.6-amd64.deb` (choose the matching file). The stable `athan-time` package identity supports fresh installation and upgrades. |
| macOS | Build the matching Intel/Apple Silicon DMG on macOS. Open it and copy **Athan Time.app** to the same Applications folder, choosing Replace for the existing bundle. |

Windows interactive setup offers Retry/Cancel when it cannot write the existing
executable. Silent `/S` setup returns exit code 2 in that case. It does not
terminate the application. A fresh install accepts `/D=C:\Path with spaces` as
its last argument, without quotes around that argument; registered updates keep
their original path. Re-running the same setup can replace the bundled files.
Silent setup defaults to Only me on a fresh computer; `/CurrentUser` and
`/AllUsers` select a scope explicitly (mutually exclusive). With no explicit
scope, an existing user copy takes priority, otherwise an existing machine copy
is updated. Explicit selection can install/update the other scope in a different
folder; the two scopes have independent registration/shortcuts. This is not an
automatic migration between scopes. Reusing another scope's marked folder or an
unregistered application folder is rejected. All-users silent setup still needs
administrator approval unless the invoking process is already elevated.

Uninstall removes that package's known files, shortcuts and registration,
preserving the profile and unrecognized user-added files; an installation folder
containing user files can therefore remain. Uninstalling does not remove a login
entry that points to another installation. An installer-owned `athan-install.ini`
records the directory's scope for uninstall; old unmarked installations retain
legacy current-user behavior. Machine uninstall does not inspect or change
any user's login entries; each user should disable **Start when I sign in** before
shared removal. Per-user uninstall removes only its own matching startup entry.
An older Windows uninstaller also leaves the shared uninstall registration and
Start Menu shortcut intact when they belong to another registered installation.

For Linux/macOS removal, first disable **Start when I sign in** in General, then
exit the app. Linux: remove the `athan-time` package through the package manager.
macOS: remove **Athan Time.app** from its Applications folder. These operations
leave per-user settings in place; package removal does not scan/edit every user's
autostart/LaunchAgent files. Keep personal recordings outside the application
bundle/install folder so bundle replacement/package removal preserves them.

The native Windows regression procedure is
[`packaging/windows/Test-InstallUpdate.ps1`](../packaging/windows/Test-InstallUpdate.ps1).
It refuses to run outside the Windows Sandbox `WDAGUtilityAccount`. In a fresh
Sandbox, map a dedicated test folder to `C:\Smoke` containing the script,
optionally `previous-1.0.0.exe`, the current versioned setup and a copy of
`AthanTime.Net/Data/default-settings.json`. Use the script as the Sandbox logon
command. Pass `-Installer` explicitly or map exactly one current versioned setup;
the expected release is derived from its filename. It writes
`progress.txt`/`result.json` into that mapped folder; optional
`-CloseSandbox` shuts down only the guest after recording the result. The prepared
configuration for this host is under ignored `artifacts/installer-smoke-1.0.1/`.
If the previous package is absent, the procedure simulates its missing
InstallLocation/scope-marker registry shape and reports that limitation instead
of claiming an actual old-package upgrade. `-TestAllUsers` adds machine-scope
checks and requires an elevated Sandbox PowerShell. It checks Program Files,
HKLM/common shortcuts, same-scope updates, cross-scope folder rejection,
coexistence, and preservation of user settings/startup/registration during machine
uninstall. This switch does not test UAC itself: separately exercise interactive
Only me, machine elevation and UAC cancellation/alternate credentials in a
disposable standard-user session. Never run these procedures in the host profile.

For destination controls without installing, map the final setup and
[`Test-InstallerDestination.ps1`](../packaging/windows/Test-InstallerDestination.ps1)
into a fresh Sandbox and run the latter with `-Installer` and `-ResultFile` guest
paths. It refuses the host profile, checks that fresh destinations do not exist,
reads the real edit control/caption with bounded `WM_GETTEXT`, verifies the full
release version in the caption, and cancels every setup
without clicking Install. It checks user/machine defaults, `/D=` paths with spaces
and a simulated registered user update's read-only/Browse state. Machine cases
need an elevated guest and are reported as skipped otherwise. This complements
source-contract tests; it does not verify browsing, Back/scope changes, UAC or
actual installation/removal. Keep result JSON with the package checksum.

This checks fresh installation, upgrade from 1.0.0 in a path with spaces, settings/
recording/startup preservation, same-version reinstall, locked/running app
protection, the installed app's single-instance/restart behavior, stale-uninstaller
ownership and uninstall preservation/cleanup. These are native integration checks, never unit tests;
no real host profile/startup entry should be changed. Linux/macOS install/replace
and version-upgrade checks still require their matching disposable OS environments.

## Debian/Ubuntu package

On Debian or Ubuntu with the .NET 10 SDK and `dpkg-deb`:

```bash
chmod +x packaging/linux/build-deb.sh
./packaging/linux/build-deb.sh
```

Defaults come from the shared version and native CPU. To override, supply the
version and `amd64`/`arm64`, for example `./packaging/linux/build-deb.sh 1.0.2-beta.6 arm64`.

The DEB declares native .NET/UI/audio dependencies for Ubuntu 22.04 and newer
and recent Debian releases. It uses X11 (XWayland on a Wayland desktop).
Check dependency availability before distributing to a new release. See the
[.NET Ubuntu dependencies](https://learn.microsoft.com/en-us/dotnet/core/install/linux-ubuntu-install)
and [Avalonia platform requirements](https://docs.avaloniaui.net/docs/supported-platforms).

## macOS application and DMG

On macOS with the .NET 10 SDK:

```bash
chmod +x packaging/macos/build-app.sh
./packaging/macos/build-app.sh
```

Defaults come from the shared version and native CPU. To override, supply the
version and RID, for example `./packaging/macos/build-app.sh 1.0.2-beta.6 osx-x64`.
The script creates an `.app` bundle and a DMG with
Apple's built-in `hdiutil`.

The DMG contains the `.app` and an Applications shortcut; drag to install.
The minimum macOS version is 14, matching Avalonia's current free-support range.
All native scripts use isolated staging directories to avoid packaging obsolete files.

## Desktop testing from Windows

For development on Windows 11 Pro, use an Ubuntu Desktop VM in Hyper-V for Linux
installation, login, tray and repeat-launch checks. Ubuntu 24.04 x64 is one
declared target, not coverage for ARM64 or every Linux desktop. Keep tests in a
disposable guest profile and use the final self-contained DEB; the .NET 10 SDK is
needed in a guest only when building source. Run the
[manual smoke checklist](RELEASE-CHECKLIST.md#manual-native-smoke-tests), including
repeated hidden/minimized window restoration. Ensure a usable guest audio device
exists before interpreting playback results; virtual graphics/audio still need
physical-hardware follow-up where relevant. See
[Microsoft's Hyper-V setup](https://learn.microsoft.com/en-us/windows-server/virtualization/hyper-v/get-started/install-hyper-v).

WSL2/WSLg is useful for Linux builds, automated tests and initial GUI checks,
but its Windows-integrated desktop is not evidence for normal Linux shell/tray,
login or compositor behavior. See [WSLg](https://learn.microsoft.com/en-us/windows/wsl/tutorials/gui-apps).
macOS desktop checks require access to an appropriate local/remote Mac; WSL does
not run macOS. Follow the applicable [Apple license](https://www.apple.com/legal/sla/).
Verify Intel x64 and Apple Silicon ARM64 separately. The optional CI workflow
can build/test/package those targets, but does not replace interactive desktop
checks or change the local-Git-only workflow. No VM, hosted service or CI job was
configured or run as part of documenting these options.

## Compatibility and validation limits

Compatibility review (2026-10-10): the new annual-rule editor adds no package or OS API; Noda Time and Avalonia handle dates/controls identically across the five existing RIDs. Automated multilingual/headless tests are not native OS certification.
The editor is checked at the 980×600 minimum size in all eight languages and both themes; English/Arabic renders were visually reviewed, including readable hour/minute fields and RTL ordering. Unit tests cover recurrence, leap/month-end handling, cross-year seasons, exact UTC boundaries, JSON migration, invalid seasons, and reminder/countdown durations. Native smoke tests below remain outstanding.
Automated checks (2026-10-10): Debug and Release builds each completed with zero warnings/errors; all 179 tests passed in each configuration. Coverage includes detached Apply/Cancel drafts, per-location migration, JSON recovery/import/export, restart/sleep/clock-jump reminders, all-eight-language navigation and readable controls. Tab surfaces, selected fills, actual header colors, stable selections and RTL bounds are checked at 980×600 in both themes; pointer/keyboard selection is tested headlessly. English/Arabic tab-navigation renders were visually reviewed alongside the earlier Today, General and seasonal-editor captures. Native OS smoke tests remain outstanding.

| Desktop target | Packaging baseline / validation still needed |
| --- | --- |
| Windows 11 x64 | Current supported Windows 11 build; verify setup, tray/audio and editor on the target machine. |
| Windows 10 x64 | Avalonia lists 22H2/build 19045 as best-effort Tier 2. Microsoft now limits .NET 10 Windows 10 support to qualifying LTSC/Enterprise editions; do not promise full support for all Windows 10 editions/builds. Native testing is required. |
| Ubuntu x64/ARM64 | Target 22.04/24.04 or a supported newer release with the declared native dependencies and X11/XWayland. Verify GTK/file picker, fonts, tray and audio. |
| macOS x64/ARM64 | macOS 14+; verify app launch, font/RTL rendering, tray and audio on Intel and Apple Silicon. |

Reviewed against [Avalonia platform support](https://docs.avaloniaui.net/docs/supported-platforms), [Microsoft Windows/.NET support](https://learn.microsoft.com/en-us/dotnet/core/install/windows), and [Ubuntu/.NET dependencies](https://learn.microsoft.com/en-us/dotnet/core/install/linux-ubuntu-install). Runtime compatibility and vendor support are distinct; this review does not replace native smoke tests.

Validation (2026-10-04): Debug/Release builds and tests pass. Cached,
framework-dependent publish checks passed for all five RIDs, including the Ogg,
NVorbis and native audio presence guards. These are not self-contained installers.
Self-contained verification was interrupted by a runtime-pack download timeout;
NSIS/DEB/DMG installation and real playback/tray/email still need target-OS smoke tests.

The new manual CI workflow and release checklist (2026-10-10) provide a reproducible
validation path; the workflow itself has not run from this workspace. Windows CI
uses a Windows Server runner, not Windows 10/11 desktop certification. Ubuntu ARM64
and both macOS architectures have separate matching-host jobs. All interactive
native checks remain **UNVERIFIED** until recorded in the release checklist.

## SQLite location catalog (2026-10-22)

The app now ships `Data/locations.db` rather than embedding the full location JSON.
It opens the database read-only, uses independent unpooled connections with a
2 MiB page-cache limit, fetches the saved city by indexed identifier and limits
search results before deserialization. The builder creates the FTS5 trigram index;
two-character searches scan on the existing search worker. Unicode normalization
is shared with the builder rather than delegated to OS SQLite collations.
The archived GeoNames JSON remains the reproducible source; see the
[offline data update procedure](RELEASE-CHECKLIST.md#offline-data-updates).

The database contains the same 235,844 places and occupies 112,795,648 bytes
(about 108 MiB installed). This trades disk space for startup speed and memory.
Settings/import/export remain JSON. Microsoft.Data.Sqlite 10.0.12 brings the
SQLitePCLRaw 2.1.12 native bundle, with no system SQLite/server dependency.
Publish guards require `Data/locations.db`, the managed provider and matching
`e_sqlite3.dll`, `libe_sqlite3.so` or `libe_sqlite3.dylib` beside the executable.

An isolated Release location-service probe on Windows build 10.0.26300 x64,
.NET SDK 10.0.402, measured opening the catalog plus fetching Beirut at 103 ms,
with about 0.3 MiB live managed memory. Beirut's first search took 7 ms; a
two-character query took 192 ms and a broad country query 199 ms. Across these
queries live managed memory remained 0.3–0.4 MiB; process private memory reached
13 MiB. The earlier JSON probe measured 1,433 ms and 97.9 MiB live managed memory
for loading, then 1,232 ms and 156.8 MiB after the first search. These are local
service-only measurements, including cold provider initialization, not full UI
startup or whole-application memory guarantees.

Debug and Release builds passed with zero warnings/errors; all 211 tests passed
in each configuration. Added comparisons cover all eight languages, ranking,
two-character queries, accents, case, literal wildcard characters, limits and
concurrent connections. Follow-up regressions restore correctly encoded Unicode
inputs, require known nonempty matches in every supported language, compare saved
city coordinates/zone IDs/translations with archived JSON and reject missing
catalogs without creating files. The all-city TZDB check reads the shipped SQLite data.
Native installation and actual macOS/Linux execution still require matching-OS
smoke tests; cross-publishing alone cannot establish those results.

Framework-dependent Release cross-publish checks passed for `win-x64`,
`linux-x64`, `linux-arm64`, `osx-x64` and `osx-arm64`. Native PE/ELF/Mach-O
architecture headers were verified, with identical database SHA-256 in every
output: `281F0C846FF830B4182F4C73459C68E6F4113F2245939AF39FA226F4E541A499`.
Apple Silicon used `UseAppHost=false` for this deployment check after NuGet's
`Microsoft.NETCore.App.Host.osx-arm64.10.0.13` download ended prematurely;
its launcher and self-contained installer remain unverified. Production publish
settings still request the normal launcher. The other four checks included
their normal framework-dependent launcher. SQLite ran in the Windows x64
automated tests/probe; other OS native execution was not performed.

## Shared secondary-window appearance (2026-10-23)

`Styles/AppStyles.axaml` supplies the palette, typography, card surfaces and control
sizing for the main window and app-owned secondary windows. `AppWindow` binds live
theme/direction and supplies the shared icon/card framing for exit/import
confirmations, information dialogs, prayer reminders and text-Adhkar windows.
Reading popups remain non-activating; reminder expiry and close behavior are unchanged.
OS file pickers and native tray menus retain their platform appearance.

On Windows build 10.0.26300 x64 with SDK 10.0.402, Debug and Release builds completed
with zero warnings/errors and all 239 tests passed in each configuration. The
headless host now loads the production shared styles and Compact Fluent density.
New UI checks compare window/card/text colors, fonts, button sizing, theme/direction
changes, reminder dismissal and exit cancellation in all eight languages and both
themes. English dark prayer/Help and Arabic light exit/reading captures were visually
reviewed; captures are generated under the test output's `ui-captures/popups` folder.
Native popup/focus behavior, OS file pickers and actual macOS/Linux execution remain
matching-OS smoke checks. Cross-platform packaging was not rerun for this UI change.

Button-style follow-up (2026-10-23): all app-owned action buttons now use the
shared green palette, rounded bordered shape and themed hover/pressed/disabled
colors. A rounded accent border supplies keyboard focus without a duplicate
rectangular adorner. Fluent mouse/keyboard behavior is preserved. Debug/Release
builds passed with zero warnings/errors; all 240 tests passed in each configuration.
Added checks exercise actual rendered labels, enabled-state contrast, mouse/Space
activation, disabled clicks and keyboard focus across all eight languages and
both themes. Normal/pressed, disabled and Arabic dark keyboard-focus captures
were visually reviewed. Native OS smoke-test limits above still apply.

## SVG icon source (2026-10-23)

`Assets/athan-icon.svg` is the editable vector master. The development-only
`tools/IconBuilder` renders PNG and ICO sizes directly from its path,
using the existing SkiaSharp 3.119.4 version. Its native packages cover Windows,
macOS and Linux; no SVG package or icon-conversion process was added to the app.
Ordinary builds/publishes consume the tracked exports. The icon's flat, single-path
SVG format and regeneration commands are documented in
[icon notes](../AthanTime.Net/Assets/ICON.md).

Verified on Windows build 10.0.26300 x64, SDK 10.0.402, working tree based on
`35c2974`: Debug/Release builds passed with zero warnings/errors and all 243 tests
passed in each configuration. New asset tests check the self-contained vector,
mint fill, transparent openings and decodable ICO sizes. Regeneration followed
by `--check` passed; missing/stale staged exports correctly failed without writing.
The Windows icon decoder accepted the ICO. Native-size 16/24/32-pixel previews
were visually reviewed against white, light gray, dark, blue and green backgrounds.
The initial exported PNG was 43,261 bytes and the ICO 30,426 bytes.

Actual tray rendering, macOS/Linux execution and native installers were not
revalidated for this asset change; they remain matching-OS smoke tests. No new
Dock-icon packaging behavior is claimed.

Native-icon follow-up (2026-10-23): the running app had still loaded the 910-pixel
PNG, taking Avalonia's Windows bitmap fallback instead of selecting ICO frames.
All native app icons now use `App.CreateDesktopIcon`: ICO on Windows, a compact
64-pixel PNG on macOS/Linux. A separate 32-unit SVG variant gives icons up to
64 pixels a solid minaret without subpixel window/finial details. Fourteen ICO
frames cover common title-bar/taskbar DPI sizes; detailed artwork remains for
larger sizes and the Linux launcher. The compact PNG is 2,921 bytes and the ICO
46,999 bytes.

On the same Windows x64/SDK host, Debug/Release builds passed with zero
warnings/errors and all 245 tests passed in each configuration. Regression
checks cover both SVGs, the compact PNG, native-frame decoding/transparency and
an unbroken small minaret. The generator's `--check` passed. An isolated check
using the production icon factory and real Avalonia Win32 backend verified exact
small/big native frame selection at 100%, 125%, 150%, 200%, 250%, 300% and 400%
DPI (16–64 px small, 24–96 px big). It did not launch the app or load a profile.
Native-size 16/20/24/32-pixel previews were reviewed against five light/dark/colored
backgrounds. Full interactive tray/window testing and macOS/Linux execution still
require matching-OS smoke tests; existing processes must exit/relaunch to load
the rebuilt resources.

## Startup investigation (2026-10-23)

The reported slow launch occurs from Visual Studio 2026. On Windows build
10.0.26300 x64 / SDK 10.0.402, an isolated native Avalonia probe used the real
host, styles, fonts, SQLite catalog, view model and main window, with temporary
settings and fake audio/login services. It measured construction/layout and an
offscreen render, not Visual Studio debugger attachment or the actual first
visible tray/window frame. No user profile or IDE settings were modified.

| Stage | Repeated Release runs | Repeated Debug runs, no debugger |
| --- | --- | --- |
| Host construction | 193–219 ms | 201–232 ms |
| Initial settings/logging configuration | 152–169 ms | 143–190 ms |
| Avalonia platform/fonts/styles | 673–676 ms | 668–677 ms |
| SQLite repository initialization | 30–33 ms | 31–36 ms |
| Remaining view-model initialization | 60–62 ms | 60–72 ms |
| Window construction/bindings | 475–560 ms | 543–569 ms |
| First layout/templates | 297–330 ms | 363–366 ms |
| Through first offscreen render, total | 1.93–2.09 s | 2.07–2.21 s |

The first observed run immediately after building was slower: 7.97 s Release and
6.53 s Debug, dominated by host/platform initialization. This shows a first-run
effect; it does not establish whether JIT, disk cache, security scanning or another
machine condition caused the difference. A separate incremental Debug solution
build took 3.56 s wall time (3.16 s reported by MSBuild), before any application
launch. The probe's Debug build took 4.31 s. These CLI build timings are not
measurements of Visual Studio's own build/attach pipeline.

The repeatable application cost is mainly Avalonia and window initialization.
SQLite remains bounded/on demand; audio devices open only for playback, and SVG
conversion remains a development-only operation. The measured results do not
identify a location-catalog or icon-conversion startup regression. Build/first-run
effects and debugger overhead are plausible explanations for the longer F5 wait;
the actual attached-debugger delay was not captured. No application optimization
or performance improvement is claimed from this investigation.

For a fair Visual Studio comparison, build once, compare F5 with Start Without
Debugging (Ctrl+F5), then launch the same built executable directly. A faster
Ctrl+F5/direct run isolates the debugger portion. Review Just My Code and module
symbol loading if that difference is large; do not reset global IDE settings or
disable protections as a blanket performance fix. See Microsoft's
[debugger startup guidance](https://devblogs.microsoft.com/devops/make-debugging-faster-with-visual-studio/)
and [Just My Code documentation](https://learn.microsoft.com/en-us/visualstudio/debugger/just-my-code).
Keep startup benchmarks separate from machine-independent unit tests.

Only documentation changed in this investigation. The Release/Debug probe builds
and incremental Debug solution build passed; the full test suite was not rerun.

## Sunrise wording (2026-10-24)

Shoroq/sunrise reminder controls, timing caption and before/at/after popup text
now use sunrise-specific captions in all eight languages. The shared timing help
uses neutral event wording. This is a wording change: sunrise time, optional
audio/reminders, stored choices and UTC reminder identities retain their behavior.
See the [product contract](REQUIREMENTS.md#required-behavior).

On Windows build 10.0.26300 x64 / SDK 10.0.402, Debug and Release builds passed
with zero warnings/errors; all 249 tests passed in each configuration. Headless
UI checks render both prayer/sunrise controls in all eight languages and both
themes, retaining row identity, numeric values and independent switches. Reminder
checks cover all three sunrise moments, translated titles/messages, localized
time formats, unchanged audio dispatch and persisted sunrise identities. JSON
caption parity checks cover every new entry in all languages. Native OS smoke
tests were not rerun for this wording change.

## Signing limitation

Recipes remain free; builds are unsigned unless signing is configured. Windows
Smart App Control can block unsigned code, including DLLs and the uninstaller's
temporary executable. Optional Windows build/payload/installer/uninstaller signing
now uses a provider-issued certificate and the Windows SDK SignTool; see
[Windows signing setup and current limits](WINDOWS-SIGNING.md). No trusted
certificate is available on this host, so the existing installer remains unsigned
and its blocking issue is not yet resolved. macOS signing/notarization remains a
separate Apple Developer process. Native target-OS verification is still required.
