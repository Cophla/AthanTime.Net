# Athan Time

A private, offline Islamic prayer-time and Athan application for Windows, Linux
and macOS, built with Avalonia and .NET 10. It displays the five prayers and the
Shoroq/sunrise time marker, with independent before/at/after reminders, eight
languages, on-demand SQLite location search, offline IANA time-zone rules and
JSON settings. Release permits one running instance per OS user; launching again
opens its existing window. Shoroq is shown as sunrise, never described as a prayer.

Current release: **1.0.2-beta.7 (Beta)**. The main header/window title identifies
the beta in all eight languages; About and support drafts retain the full release
version. Setup/package names also identify the beta. See [release-channel packaging](docs/PACKAGING.md#beta-release-2026-11-03)
for native version formats and verification limits.

## AI agents and contributors

Start with [AGENTS.md](AGENTS.md), then [product requirements](docs/REQUIREMENTS.md).
These are the canonical engineering/product instructions for every AI agent;
read only task-relevant specialist documents. For every change, follow the
[change completion rule](AGENTS.md#change-completion-rule) to keep affected tests,
comments, descriptions, rules and related files synchronized. Link to the canonical
guide instead of creating duplicate vendor-specific rules or memory files.
Keep code direct and explainable under the [simplicity rule](AGENTS.md#working-rules),
reusing existing helpers and retaining tested lifecycle/platform safeguards.
Every task must consider Windows, macOS and Linux capability under the
[mandatory cross-platform rules](AGENTS.md#working-rules).
The [code simplicity/resource review](docs/CODE-SIMPLICITY-REVIEW.md) records
repository-wide scope, measured evidence, resolved search/settings findings
and the native resource checks still needed; it does not certify a full-app
RAM/CPU/GPU budget.
Latest verified build/test evidence and native-test limits are in
[packaging validation](docs/PACKAGING.md#support-mailbox-beta7-2026-11-08).
Copy-ready GitHub prerelease details are in the [beta.7 release notes](docs/releases/v1.0.2-beta.7.md).

## Included

- Five prayer times (Fajr, Dhuhr, Asr, Maghrib, Isha), a separate Shoroq/sunrise marker, next-prayer countdown, Islamic midnight, qibla, and adjustable Hijri date.
- 11 Sunni calculation presets, Standard/Hanafi Asr, three high-latitude rules, and explicit per-location ±60-minute corrections including sunrise.
- Prayer astronomy and qibla from the free MIT-licensed Adhan library.
- Bundled offline IANA summer-time rules, custom annual calendar/weekday schedules, per-zone manual override, date navigation, independent before/at/after reminders including sunrise, and native tray status.
- JSON-backed regional convention selection for every location through country rules and a documented MWL fallback; manual override remains available.
- A read-only SQLite catalog of 235,844 offline places in 252 countries, with coordinates, time zones, population ranking and localized names where GeoNames supplies them. Locations are queried on demand; startup does not deserialize the entire catalog.
- English, Arabic, French, Spanish, Turkish, German, Indonesian, and Urdu UI with stable runtime LTR/RTL switching and light/dark modes. App-owned dialogs and reminder popups share the main window's colors, fonts, cards and controls.
- JSON configuration and settings, default dependency injection, and optional structured Serilog file logs in the per-user `AthanTime/Logs` folder. Turning logging off deletes the app's own logs and removes the folder when empty.
- Saved manual coordinate locations with explicit IANA time zones.
- Bundled CC0 Athan, Takbeer and short Adhkar Ogg Vorbis recordings; Ogg-only custom per-prayer audio, sequential/random Adhkar audio, eight-language text-Adhkar reading windows, and tray pause/enable controls. See [audio help](docs/AUDIO.md).
- Help/About screens, exit confirmation, one running Release instance per OS user on Windows/macOS/Linux, concurrent Debug development runs, opt-in minimized start on user login, centralized semantic versioning, and a one-line .NET target upgrade point.
- Focused Today page and four settings groups: Location, Prayer & clock, Audio & Adhkar, General. Eight-language seasonal preview with Apply/Cancel and Restore automatic.
- Automatic last-known-good JSON backup/recovery, visible storage warnings, confirmed settings import/export, and restart-safe reminder deduplication.
- Native install/update recipes preserve the user profile and use the shared version/host CPU by default. Windows offers Only me or All users, retains the registered scope/folder on update, checks for locked files, and preserves user-added files during uninstall; stale uninstallers preserve another installation's registration. Settings and login startup remain per-user. See [installation and updates](docs/PACKAGING.md#installation-and-updates).
- No runtime network calls, accounts, API keys, paid services, or proprietary components.

## Run and verify

Requires the free .NET 10 SDK.

The detailed and small desktop icons have editable SVG sources. A development-only
cross-platform tool generates Windows ICO frames, a compact macOS/Linux desktop
PNG and the larger Linux launcher PNG. See
[icon regeneration](AthanTime.Net/Assets/ICON.md); ordinary builds use the tracked
exports and do no runtime SVG conversion.

```powershell
dotnet run --project AthanTime.Net/AthanTime.Net.csproj
dotnet build AthanTime.Net.slnx
dotnet test AthanTime.Net.slnx --no-build
dotnet build AthanTime.Net.slnx -c Release
dotnet test AthanTime.Net.slnx -c Release --no-build
```

Debug builds (the default `dotnet run`, or an IDE's Debug configuration) allow
multiple instances on Windows, macOS and Linux, including alongside an installed
Release copy. Use `-c Release` to test the shipped single-instance behavior.
Launching Release again restores and activates the existing main window,
including when hidden in the tray. The second process exits after its request;
it does not create another set of reminders or audio services. The running copy
must include this activation support. OS focus/elevation/session restrictions
can limit activation; a failed handoff keeps the single-instance guard enforced.
Changing `DOTNET_ENVIRONMENT` or diagnostic logging does not bypass Release's
guard. Concurrent development runs share the user's settings/logs and each runs
its own reminders/audio; simultaneous settings edits can overwrite one another.

Tests use xUnit v3 with constructor dependency injection and Avalonia.Headless.
All UI captions share one embedded `Data/ui-text.json` catalog. Tests require
exact key parity across the eight languages and preserve case-insensitive lookup,
English fallback and visible unknown keys.
Location regressions compare SQLite searches and selected-city fields with the
archived GeoNames JSON, covering all eight languages, accents, short queries,
literal wildcard characters, ranking, limits and concurrent searches. The JSON
reference is test-only; the application ships the prebuilt SQLite database.
Missing-catalog tests require a visible failure without creating a database or
falling back to an obsolete seed catalog. Device-free audio tests cover all
bundled recordings, stable IDs, empty reads, EOF seeking and disposal.
Tests use temporary profiles and fake native integrations. Native installation
and desktop behavior still need matching-OS smoke tests; see
[validation evidence and limits](docs/PACKAGING.md#compatibility-and-validation-limits).
With a Windows-only development computer, see
[desktop testing from Windows](docs/PACKAGING.md#desktop-testing-from-windows)
for Hyper-V, WSLg and macOS access boundaries.
Reminder checks include sunrise-specific wording in all eight languages, unchanged
before/at/after behavior, independent switch combinations and saved durations,
restart suppression for all three moments, and sunrise's exclusion from the
next-prayer countdown. Host tests verify that resolving the application keeps
window construction deferred until its factory is invoked.
Single-instance checks launch an isolated process helper using the production
guard/channel: Release duplicates, concurrent activation, delayed UI readiness,
normal/forced exit and abandoned ownership are covered, alongside Debug coexistence
in both launch orders, without accessing the real profile/startup entry. Headless
App tests verify that requests restore the same hidden/minimized window.
Headless UI checks cover shared popup/button styles, RTL, themes and stable values;
icon checks cover both SVG sources and generated native sizes.

When assessing startup, time the built app separately from IDE build/debugger
launch. The [startup investigation](docs/PACKAGING.md#startup-investigation-2026-10-23)
records local measurements and their limits; they are not cross-platform guarantees.
The one-second clock/reminder loop reuses schedule scans and raises UI updates
only for changed values. The [resource measurements](docs/PACKAGING.md#idle-resource-reduction-2026-11-04)
describe lower managed allocations/work in isolated tests; they do not measure
the complete application's RAM, GPU or desktop idle CPU consumption.
Location search keeps one active query and only the latest pending request;
obsolete results are discarded. Repeated equivalent volume values skip settings
writes, while actual changes still save immediately. Selected-date and live-tray
display updates use separate private methods within the same view model.

Distribute one normal Release package per operating system. Debug/Release are developer build configurations, not user editions. In that same installed app, **Save logs** enables/disables file logging, and **Diagnostic logging** chooses extra detail while logging is enabled. Both take effect immediately and are saved per OS user in `AthanTime/settings.json`. Files are in the OS application-data `AthanTime/Logs` folder; turning logging off closes the file and deletes Athan Time logs. The `appsettings.Development.json` and `appsettings.Release.json` files only set developer/runtime environment defaults.

## Local version control

This project uses a local Git repository on branch `main`. No remote or online
account is required. Review work with `git status`, `git diff` and
`git log --oneline -5`; save a reviewed change with `git add <paths>` followed by
`git commit -m "Describe the change"`. Commit author settings belong to this
repository's local Git configuration.

For a new empty repository, use the reviewed clean source ZIP/folder described
in [GitHub readiness](docs/GITHUB-READINESS.md). It contains current source and
compressed data without old Git history, local profiles or build outputs.
Do not copy the original `.git` folder: it retains the old oversized database
and earlier commit email. Set the new repository's public commit identity locally.

Build outputs, test/coverage reports, diagnostic dumps/traces, macOS debug symbols,
editor temporary files, IDE user settings and the GeoNames download cache
are ignored. Local publish profiles and private signing keys also stay outside
version control. The prebuilt `Data/locations.db.zip`, archived source JSON,
bundled audio, licenses and regeneration tools are versioned together. MSBuild
extracts the catalog incrementally into configuration/RID-specific `obj` folders
and ships `Data/locations.db`; ordinary builds need no external unzip utility,
Git LFS or catalog download. The raw database generated by the data builder stays
ignored. `.gitattributes` keeps
shell/macOS launcher scripts on LF and C#/Windows scripts on CRLF across checkouts;
`.editorconfig` uses the same conventions when editing source files.

## Package

Local IDE publish profiles are ignored; use the shared packaging commands below
for distributable installers. A developer's Windows Debug/single-file profile
does not define the cross-platform release configuration.
Windows setup displays the selected destination: Only me defaults to
`%LOCALAPPDATA%\Programs\Athan Time`, and All users defaults to 64-bit Program
Files. Fresh installs can browse; updates show their existing folder read-only.

The adopted [deployment policy](docs/PACKAGING.md#deployment-policy) keeps one
shared C# codebase and per-OS self-contained packages, including .NET and matching
native dependencies. Framework-dependent packages and a custom universal
installer are deferred while native validation and trusted signing take priority.

Double-click [Create-Installers.cmd](Create-Installers.cmd) on Windows or
[Create-Installers.command](Create-Installers.command) on macOS. On Linux, run
[Create-Installers.sh](Create-Installers.sh) in a terminal. Each builds the current
OS's native Release installer into `artifacts/packages`. Install the .NET 10 SDK;
Windows automatically caches verified portable NSIS when it is missing. Other OS
packaging prerequisites are in [one-click setup](docs/PACKAGING.md#one-click-launchers).

Feedback opens a localized draft in the user's mail app. Email logs first exports
only application logs to a ZIP for review and manual attachment; nothing is sent
automatically. Support, suggestions, complaints and log-sharing drafts are
addressed to **athantimedotnet@gmail.com**, the public project mailbox configured
by `Application:SupportEmail` in `appsettings.json`.

Free portable, Windows setup, Debian package, and macOS app/DMG recipes are in [docs/PACKAGING.md](docs/PACKAGING.md).
Use the [release checklist](docs/RELEASE-CHECKLIST.md) for offline data updates and native smoke tests. The optional manual workflow builds/tests/packages five matching RIDs; it does not certify interactive tray, audio or installation behavior.

Linux tray support depends on the desktop environment's StatusNotifier/AppIndicator support. Closing minimizes on Linux so a missing tray never makes the application inaccessible. Windows and macOS use their native integrations; Exit is also available in the main window.

Login startup and Start minimized hide/minimize only the initial opening. Click
the tray icon or choose Show to restore the existing window; it stays open and
can be hidden/restored repeatedly without changing the startup preference.

The main window remembers its last normal position for the current OS user.
First launch is centered; later launches and tray restores keep the window on an
available display, recovering safely when monitors change. Position is saved in
`AthanTime/window-position.json` in the OS application-data folder, separately
from settings exports. Window size and maximized state retain their existing
behavior. Absolute placement depends on the desktop window manager/compositor.

## Accuracy model

Adhan calculates prayer times locally with high-precision Meeus astronomical equations. Inputs are coordinates, local civil date, bundled IANA time-zone rules, selected preset, Asr school, high-latitude rule, and visible minute corrections. Seasonal clock conversion never modifies Adhan's solar calculations or adds hidden prayer corrections.

Calculated schedules are not official observed timetables. Ministries and mosques may use observation, elevation, safety margins, or city-specific corrections. Hijri dates can also vary by moon sighting, so a visible ±2-day correction is provided.

Prayer corrections are saved by stable location ID (including sunrise), not translated names. Selecting a new city starts at zero unless you already saved corrections there. Existing profiles migrate their corrections only to the previously selected city. The Hijri day adjustment remains global.

After sleep or a backward/>5-second timer gap, missed reminders are skipped and Adhkar starts a fresh interval. An event exactly at resume may run. The latest 128 invariant UTC reminder identities survive restart, preventing repeat notifications/audio within a prayer minute; a first launch may notify in the current minute if not already recorded.

## Prayer reminders

Before-prayer popups default to **15 minutes**. Choose **30 minutes** or any value
from 1 to 180 independently for each prayer. Notification settings provide
separate switches for **before**, **at prayer time**, and **after**. After-prayer
minutes are independently configurable (default 5, initially disabled).
Existing saved values are retained; Athan audio still starts at prayer time. See the
[reminder settings steps](docs/AUDIO.md#prayer-reminders).
Shoroq/sunrise retains its own optional reminders and audio, initially disabled.
Its controls and popups say sunrise rather than prayer; its displayed time and
saved choices are unchanged. It is excluded from current/next-prayer countdowns.

## Settings backup and transfer

General settings provides JSON export/import. Imports are validated and require confirmation; this device's login-startup, logging choices and reminder history stay unchanged. Audio files must be copied separately. Exported JSON includes saved coordinates and local audio paths, so review it before sharing. The app keeps a validated `settings.json.bak`; malformed settings recover without silently overwriting the damaged file. Recovery/write failures appear on every page. A newer schema is never overwritten by an older app.

`Data/prayer-conventions.json` selects a commonly used method, Asr school, and high-latitude rule. It deliberately applies no minute correction. Countries without a specific verified mapping inherit the documented MWL fallback, so every offline city resolves deterministically without pretending that one timetable is official everywhere.

## Offline data updates

### Summer time

Every bundled city uses its own IANA zone through Noda Time's embedded offline database, consistently across Windows, Linux and macOS. **Automatic (offline rules)** is the default: the seasonal checkbox follows that zone's rules, not a country-wide assumption. The panel shows all start/end/change times for the selected year and the database version. Recurring rules are evaluated for each year; southern seasons, half-hour changes and Ramadan clock changes are supported. Zones without changes are labeled accordingly.

For today the checkbox follows the actual instant; for another selected date it describes noon. Prayer conversion always uses the rule at each prayer's instant. Countdowns, reminder durations and Islamic midnight use elapsed UTC time, including nights when clocks change.

Choose **Custom annual dates** to set a start and end: select the month, then a calendar day or first/second/third/fourth/last weekday, and the local time **before** the clock changes. Example: start last Sunday of March at 02:00; end first Thursday of October at 03:00. These are examples, not official rules. The preview resolves dates for the displayed year. Seasons may cross New Year; a missing calendar date (e.g. 29 February) uses month end. Invalid/empty seasons are flagged and inactive, not silently replaced with automatic rules.

Choose **Manual on/off** to force a constant seasonal state. Custom and manual modes accept -180 to +180 minutes, including half-hour adjustments. The active checkbox is read-only in automatic/custom mode. Choosing automatic again ignores custom dates without deleting them. JSON `SummerTimeOverrides` stores preferences per IANA zone, independently of per-prayer `MinuteAdjustments`; old settings retain their original forced on/off behavior.

All seasonal edits are a detached preview until **Apply**. **Cancel** restores the active preference; **Restore automatic** stages offline rules for review and still requires Apply. Switching locations discards unapplied edits. Invalid drafts cannot replace the active clock; an invalid rule loaded from JSON stays visibly inactive. The panel shows the committed clock adjustment separately from the preview.

The UI expresses seasonal contribution relative to the year's lower seasonal baseline. This makes negative-IANA-savings cases (Ireland's winter and Morocco's Ramadan) readable without altering automatic civil clocks; the label is not a claim about a country's legal definition of standard time. Future government decisions require a bundled database/application update; there are no runtime downloads.

`Data/locations.db` is a bundled read-only SQLite catalog. Startup reads only the
selected city; searches use a prebuilt substring index and return limited results.
Two-character searches scan the catalog on a worker thread. No database server or
system SQLite installation is required on Windows, Linux or macOS. Settings stay JSON.
Rebuild the archived `locations.json.br`, raw SQLite and versioned `locations.db.zip` from GeoNames with:

```powershell
dotnet run --project tools/LocationDataBuilder/LocationDataBuilder.csproj
```

The builder downloads data only during development. Attribution is in `AthanTime.Net/Data/GEONAMES-LICENSE.txt`.
To rebuild SQLite/ZIP from the existing archived JSON without downloading anything, run
`dotnet run --project tools/LocationDataBuilder -c Release -- --database-only`.

## Deliberate exclusions

- No IP geolocation or online account; city search stays private and offline.
- Bundled audio is CC0 with source attribution in [third-party notices](THIRD-PARTY-NOTICES.md); users may replace each prayer recording with local Ogg Vorbis audio. The generic bundled Athan does not include a distinct Fajr-specific phrase, so users seeking that form should select their own Fajr recording.
- No claim that a calculated schedule is an official religious ruling.
- No paid installer builder. Unsigned Windows code can be blocked by Smart App Control. Optional [Windows signing](docs/WINDOWS-SIGNING.md) covers binaries, installer and uninstaller using a trusted provider certificate; credentials remain an external prerequisite.

## Version and framework upgrades

Version and target framework are centralized in `Directory.Build.props`. Change `Version` for a release. After validating dependencies with the .NET 11 SDK, changing the single `TargetFramework` value upgrades all projects.
