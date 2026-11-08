# Release checklist

Run this for every release, including data-only updates. No runtime updater or
network access is required. A completed build is not a completed desktop test.

## Every change

- [ ] Complete the canonical [change completion rule](../AGENTS.md#change-completion-rule)
  before handing off any change, including work that does not create a release.
  Confirm that affected tests, comments, descriptions, rules, AI guidance and
  supporting files reflect the final behavior; record checks and remaining limits.
- [ ] Review simplicity: use direct logic and existing helpers, remove only proven
  redundancy, and preserve lifecycle/failure/platform safeguards. Keep Git ignores
  and editor/line-ending rules aligned; check generated/private paths are excluded
  while source, tests, bundled data/media and licenses remain visible to Git.
  Check XAML/DI/serialization/interface and shared-builder references before removal;
  preserve regression coverage when merging files or duplicate tests. Captions
  belong to one JSON catalog with exact eight-language/key parity.

## Before GitHub publication

- [ ] Review working files, pending/untracked changes, all intended branch/tag
  history, commit identity and blobs above the host's file limits. Do not rely on
  `.gitignore` to sanitize history. Resolve the dated findings in
  [GitHub readiness](GITHUB-READINESS.md), including catalog storage/history.
- [ ] Check secrets/private-key patterns, personal home paths, copied profiles,
  media metadata and accidental IDE/build outputs. Preserve third-party credits.
  Verify exclusions and required source/data/media/licenses with
  `git check-ignore --no-index`. Review staged content before committing.
- [ ] For a fresh repository, import the reviewed source snapshot without the old
  `.git`, profiles or outputs. Include both `locations.db.zip` and
  `locations.json.br`; exclude raw generated SQLite. Check all intended new blobs
  against host limits and verify the chosen public commit identity in new history.
- [ ] Push only intended refs after review; never use a mirror push for local
  agent/internal refs. Keep remote creation and pushing explicitly user-directed.
  Confirm the intended commit-author email; do not invent a GitHub no-reply address.

## Automated checks

- [ ] Keep the adopted [deployment policy](PACKAGING.md#deployment-policy): shared
  builder, self-contained publishing and RID-matching audio/graphics/SQLite assets.
  A release must not silently become dependent on a separately installed .NET
  runtime. Native desktop checks and signing status are separate release gates.
- [ ] Set `Directory.Build.props` version; keep one Release edition per OS.
  For a beta, verify the translated label in the header/title in all eight
  languages and both themes, and the full prerelease version in About/support
  drafts, setup/uninstall display names and package filenames. Keep upgrade keys,
  bundle/package identifiers and profile paths stable. Check that the chosen
  version upgrades the preceding release and precedes the intended stable release.
- [ ] For a trusted Windows release, follow [Windows signing](WINDOWS-SIGNING.md):
  validate the provider certificate, sign/verify payload DLLs/EXEs, the NSIS
  uninstaller and final setup. Test the signed installation and temporary
  uninstaller on a disposable Smart App Control-enabled host; retain Code
  Integrity evidence. Missing credentials or a failed signing step blocks a
  signed release; an old unsigned artifact must not be reported as newly signed.
- [ ] After icon edits, regenerate and run `dotnet run --project tools/IconBuilder -c Release -- --check`.
  Keep SVG/PNG/ICO together; follow [icon notes](../AthanTime.Net/Assets/ICON.md).
  Inspect tiny-size contrast on light/dark taskbars during native smoke tests,
  including title-bar/tray/taskbar rendering at 100%, 125%, 150% and 200% scaling.
- [ ] Run `dotnet build AthanTime.Net.slnx` and `dotnet test AthanTime.Net.slnx --no-build`;
  repeat both with `-c Release`. Record date, commit/source snapshot, SDK and results.
  Verify concurrent Debug startups and coexistence with an enforced lease in both
  launch orders. With Development environment variables set, Release must still
  prevent a second host and request activation of the original window; tests use
  isolated identities and never the production mutex.
- [ ] Build native packages on each matching host using
  `dotnet run --project tools/InstallerBuilder -c Release -- --rid RID`.
  The root [one-click launchers](PACKAGING.md#one-click-launchers) use the same tool;
  check `--dry-run` and the no-argument interactive launch on each matching OS.
  For Windows, verify both installed-NSIS discovery and the checksum-verified
  portable-cache path; retain the pinned source/checksum and offline setup notes.
  Use `win-x64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`.
  Check direct recipe defaults against `Directory.Build.props` and the host CPU,
  explicit overrides, and rejection of mismatched native hosts. macOS bundle
  metadata uses the three-part numeric base; macOS display metadata retains the
  prerelease. Check Debian's `~` prerelease ordering and launcher/description labels.
  Confirm Windows signing
  includes Windows ZIP contents and excludes Linux/macOS RID payloads.
- [ ] Check that publish guards pass for bundled Ogg Vorbis, NVorbis and matching
  miniaudio, `Data/locations.db` and RID-matching `e_sqlite3`; verify licenses,
  configuration JSON and the app version in each package.
- [ ] Review all eight languages (`en`, `ar`, `fr`, `es`, `tr`, `de`, `id`, `ur`),
  both themes and minimum window size. Arabic/Urdu must be RTL, numeric fields LTR;
  language changes must preserve selections, drafts and saved values.
  Include exit/import confirmations, Help/About/information dialogs, prayer popups
  and text-Adhkar windows. Verify shared colors/fonts/cards/buttons and live theme/
  direction changes; retain native file pickers and non-activating reading windows.
  Check action-button labels, green fills, rounded borders, hover/pressed/disabled
  states and visible keyboard focus; verify mouse and keyboard activation.
  Check Shoroq/sunrise controls, timing caption and all three reminder messages:
  no prayer/Athan wording for sunrise, preserved optional settings/audio, and no
  sunrise target in the current/next-prayer countdown.
- [ ] Run recovery/import/export, old-settings migration, per-location corrections,
  recurring summer-time boundaries, UTC countdowns, restart/sleep/clock-jump and
  no-duplicate/no-catch-up reminder tests. Test failures block release.
- [ ] When investigating startup, separate build, debugger attachment, app
  initialization and first-run effects. Record host/configuration and measurement
  boundaries as in the [startup investigation](PACKAGING.md#startup-investigation-2026-10-23).
  Do not claim an optimization from measurements alone or add timing thresholds to unit tests.
- [ ] For resource changes, compare the same warmed workload/profile/settings
  before and after. Record host, build, measurement boundaries and runtime flags;
  distinguish managed allocations and tick-loop time from full-process RAM,
  CPU, GPU and native rendering. Keep one-second reminders, both seasonal
  preview/committed boundaries, day rollover and unchanged-row/cache regressions.
- [ ] To qualify whole-app resource usage, record matching-OS visible/tray idle
  CPU and memory, repeated playback/stop/popup cycles and search bursts on slow
  storage. Include custom seasons and logging choices; separate build/test tool
  costs from the installed app. Inspect pending search work and settings-write
  frequency during edits. Report open findings and missing measurements; a clean
  analyzer, passing tests or tick-loop allocation result alone is insufficient.
- [ ] For search/settings changes, use controlled slow/failing lookups to verify
  one active query, latest pending language/text, stale-result rejection, UI-thread
  updates, clear/disposal and reentrant binding callbacks. Check duplicate/clamped
  volume assignments skip saving while real edits persist immediately.

Optional: manually run `.github/workflows/desktop-validation.yml` on a public
GitHub repository for the five native build/test/package jobs and UI captures.
It is disabled for private repositories to avoid billed hosted-runner minutes.
GitHub is not required: the local commands above are the complete build path.
The workflow derives the SDK family and package version from `Directory.Build.props`
so a future `net11.0` change does not require a second framework setting.
Standard public hosted runners are currently free; review
[runner availability and billing](https://docs.github.com/en/actions/reference/runners/github-hosted-runners)
before changing that guard. CI packages are unsigned and must not be advertised
as desktop-certified simply because the jobs pass.

## Offline data updates

- [ ] Record the Noda Time package version and embedded TZDB version shown by the
  app, plus the review date. Check [IANA releases](https://www.iana.org/time-zones)
  and [Noda Time TZDB guidance](https://nodatime.org/3.3.x/userguide/tzdb). Update the
  bundled Noda Time package when needed; never substitute OS offsets or static dates.
- [ ] Run all-city zone resolution and dated seasonal-transition cases. Add a
  regression case for a government rule change; review affected city/date prayer
  UTC instants and displayed civil times. Unsupported future rules need a new release.
- [ ] For new location data, archive the exact GeoNames inputs, download date and
  SHA-256 hashes. The builder reuses `.location-cache`: move the old cache to a
  dated archive before fetching fresh inputs. Run
  `dotnet run --project tools/LocationDataBuilder -c Release` to regenerate
  `AthanTime.Net/Data/locations.json.br`, generated `Data/locations.db` and the
  versioned `Data/locations.db.zip` (build-time downloads only). To regenerate
  SQLite/ZIP from archived JSON offline, append `-- --database-only`. Commit both
  compressed inputs, not raw SQLite; retain the single `locations.db` archive
  entry, reproducible entry timestamp and archive/published database hash parity.
  Test first/unchanged/changed/missing/invalid archive builds from a clean checkout.
- [ ] Verify country/place counts, coordinates, IANA IDs, case-insensitive search
  and alternate names in every supported language. GeoNames `cities500` is broad
  coverage, not a promise of every settlement or a translation for every name.
- [ ] Run the location search/field comparisons against archived JSON, including
  known nonempty accented/non-Latin matches, two-character queries and literal
  wildcard input. Check all-city zones from SQLite. Ship the generated database
  and provider/native libraries together; keep archived JSON out of runtime loading.
- [ ] Review convention JSON against cited primary sources. Presets are regional
  calculation conventions, not official mosque timetables; never add hidden minute
  corrections or alter Adhan's solar equations to match an external table.
- [ ] Refresh GeoNames attribution, third-party notices, audio/text provenance and
  translations if inputs/dependencies changed. Ship updated data and libraries
  together in the normal offline installer, then repeat the automated checks.

## Manual native smoke tests

Use a disposable desktop account/VM and the final self-contained package. Record
the exact OS edition/build, architecture, package version/hash, display server
(Linux), audio device/backend and pass/fail evidence. Each row is **UNVERIFIED**
until a human performs the checks; never infer results from cross-publishing.
For a Windows-only development host, follow
[desktop testing from Windows](PACKAGING.md#desktop-testing-from-windows).
WSLg, an x64 VM and headless CI each cover different capabilities; do not mark
other architectures or native desktop integrations verified from those alone.

| Desktop / architecture | Result, date and evidence |
| --- | --- |
| Windows 11 x64 | UNVERIFIED |
| Windows 10 qualifying edition/build x64 (best effort) | UNVERIFIED |
| Ubuntu 22.04 x64 | UNVERIFIED |
| Ubuntu 24.04 x64 and ARM64 | UNVERIFIED |
| macOS 14+ Intel x64 and Apple Silicon ARM64 | UNVERIFIED |

- [ ] Install, launch centered on the first run without an installed .NET runtime, close/reopen,
  upgrade while preserving settings, and uninstall. Respect publisher/security
  warnings; do not bypass them. User settings removal must be a deliberate choice.
  Follow [installation/update procedures](PACKAGING.md#installation-and-updates).
  On Windows, test default and legacy custom folders, same-version reinstall,
  Only me with no elevation and All users with elevation, Program Files/HKLM64
  versus per-user/HKCU registration and shortcuts, independent copies in different
  folders, visible prefilled default destinations, editable fresh-install paths,
  read-only retained update paths, and restored editing after Back/scope changes.
  Include absent destination folders and `/D=` overrides with spaces; the
  Sandbox-only `packaging/windows/Test-InstallerDestination.ps1` reads actual
  destination/editability/Install-button state and cancels without installing.
  Also check cross-scope folder rejection and UAC approval/cancellation using both
  an administrator and a standard account (including alternate admin credentials).
  Verify that silent elevated operations relay their actual exit code, and that
  machine uninstall leaves every user's settings/login choices untouched. Include
  running-app locks from another user's session for machine updates/removal.
  Continue to check
  locked/running-app rejection, matching-only startup cleanup and preservation of
  user-added recordings during uninstall. A stale uninstaller must preserve a
  newer installation's shared registration/shortcut. Use the publish-generated uninstall
  list; never recursively delete an installation folder containing user files.
  The Sandbox-only `packaging/windows/Test-InstallUpdate.ps1` records these native
  checks; no result file means no successful verification.
  On Linux/macOS, disable login startup before removal and keep personal
  recordings outside package/bundle contents. Verify that per-user settings
  remain after removal and that reinstalling reuses them.
- [ ] Disconnect networking. Search multilingual city/country names, add coordinates,
  switch cities and verify independent corrections. Compare a dated prayer case
  using identical coordinates, calculation options, seasonal mode and corrections.
- [ ] Edit summer-time dates/first-last weekday rules, preview, Apply/Cancel and
  restore Automatic. Check an overnight/southern-hemisphere season and visible
  invalid-rule feedback without silently changing the active prayer clock.
- [ ] Check all eight UI languages/light-dark themes, file pickers and readable
  numbers/buttons. Change language while options and unsaved fields are selected.
- [ ] Verify tray status/countdown, hide/restore, exit confirmation and pause/resume;
  Ubuntu tray availability depends on the desktop's status-icon integration.
  Start with `--start-minimized` and separately with the saved Start minimized
  option. Restore through tray click and Show, wait longer than a second, then
  close/restore at least three times. The window must remain visible after every
  restore; no-tray desktops must retain their minimized taskbar route. Verify on
  Windows/macOS/Linux; scripted callback checks do not certify physical tray clicks.
- [ ] Move the normal main window, hide/restore and restart it; verify position
  persists without changing size or start-minimized preferences. Repeat with
  maximized/fullscreen/minimized states, negative-coordinate monitors, different
  DPI scales, taskbar/dock work areas and a disconnected/rearranged monitor.
  Test a missing/damaged/unwritable `window-position.json`: centered fallback or
  a usable window/save retry is required. Verify settings transfer excludes this
  device-local file and updates/uninstall preserve it with the profile. Record
  OS/window-manager placement restrictions; Linux X11/XWayland and both macOS
  architectures need their matching native checks.
- [ ] Launch twice, including simultaneous manual/login launches and a second
  executable location, while visible and while minimized/in the tray. Confirm one
  process owns the app: a duplicate restores/activates the primary window and
  exits without another window, tray icon, reminders or audio. Include a launch
  during primary startup, repeated clicks, a hidden/minimized window, channel
  failure, a pre-activation older primary and differing elevation levels. Verify
  Windows foreground handoff and OS focus/session restrictions. On macOS include Finder/Dock reopen of a hidden
  app (without a new process), startup reopen and application unhide. Debug must still
  open an independent instance. Exit normally, then force-terminate in the disposable account;
  verify a fresh launch works after each. Check independent OS users and the same
  user across login sessions where available.
- [ ] Play/stop bundled and custom `.ogg` per prayer, verify actual sound and
  idle device release, Adhkar playlist/random playback, text show/close intervals,
  before/at/after reminders and prayer-audio priority. Reject MP3 and Opus input.
- [ ] Sleep across a reminder, resume, jump the clock forwards/backwards, restart
  during the same reminder minute and cross midnight/summer-time transitions:
  no duplicate or stale queued notifications/audio, correct elapsed countdown.
- [ ] Enable then disable login startup in the disposable account; verify a real
  sign-out/login and cleanup of only this app's startup entry. Never run this check
  against the developer's real profile or from a unit test.
- [ ] Toggle logging/debug verbosity; verify only app logs are exported and disabling
  logging removes this app's log files. Verify recovery status and JSON import/export;
  review the draft/ZIP before sending email from the user's own account.

Keep failed/unavailable target tests explicit in release notes. Compatibility
baselines, unsigned-package limitations and current validation dates are in
[PACKAGING.md](PACKAGING.md).
