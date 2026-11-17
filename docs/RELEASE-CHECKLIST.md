# Release checklist

Published baseline: **v1.0.2-beta.15**. Its existing release and downloaded assets
are preserved. [Release notes](releases/v1.0.2-beta.15.md) may contain an uncommitted
local proposal; do not treat a changed working file as the published description.
Before any commit or GitHub mutation, obtain confirmation of the reviewed proposal
under [AGENTS.md](../AGENTS.md#working-rules). Never reuse earlier publication
authorization for a later description edit or replacement binary.
The native desktop, signing and recording-rights limits below remain applicable.

Keep public release text short and understandable: useful changes, download/startup
instructions, supported platforms and essential beta/security caveats. Review it for
private paths, secrets, owner conversations and unnecessary build/history details.
Keep required licenses/provenance and technical evidence in their appropriate files.
For description-only edits, check text/links and preserve the published asset hashes;
do not rebuild application packages or replace source archives.

For uninstall completion, check Only me, All users through the elevated child,
already elevated removal and setup's Uninstall action: one success message after
successful interactive removal, none after cancel/failure or in silent mode.
Dismiss the message and verify exit code zero. Use disposable Windows sessions;
Mac/Linux retain their native removal workflows and completion feedback.

Beta.13 preparation checks About summaries in all eight languages/themes, exact
project/download link targets and explicit link activation through a fake browser
action. Review rendered English/Arabic screenshots and RTL release-string order.
Keep owned About UI with the shared secondary-window base. Native browser launch
and platform signing/installer checks require matching hosts; source preparation
does not publish the release.

For the next requested release, verify Unix executable modes after extraction on
both Mac CPUs and Linux CPUs. Mac portable payloads live inside
`Athan Time.app/Contents/MacOS`; inspect their runtime files at that prefix.
Test Finder opening and the .command helper on macOS 15+, and verify that closing
Terminal leaves the app running. On Linux, test detached .sh startup and a menu
shortcut from a folder containing spaces/metacharacters. Test the optional apt
helper only in a disposable supported Ubuntu/Debian environment. Confirm all
launchers use the bundled apphost and keep the self-contained runtime guard.
Automatic DST reports need the city, comparison dates and observed prayer times.

Run this for every release, including data-only updates. No runtime updater or
network access is required. A completed build is not a completed desktop test.

For Windows setup, verify automatic graceful exit from tray and playback before update, unchanged profile/login settings, visible interactive completion, silent no-launch, both scopes and alternate-admin UAC launch identity in Sandbox. Confirm cancelled/failed setup does not launch, legacy shutdown refusal retains Retry/Cancel, and missing shell/token gives a manual Start-menu fallback. On macOS/Linux verify the documented quit/replace/desktop-launch workflow.

Before the next release, test clean per-user regional defaults (supported and
unsupported UI language, differing region/zone, Windows-to-IANA mapping, aliases,
UTC/unknown zones, 12/24-hour formats), review/confirm city in all eight languages
and both themes, and ensure existing/recovered profiles never change. Test
unchecked/checked Windows uninstall, silent preservation, All-users alternate-admin
UAC identity, busy app and cleanup failure. Verify known files are removed while
other users, recordings and unrelated files remain. In native Mac/Linux sessions,
verify consent/cancel/root refusal in the removal helper and default native
uninstall preservation. Keep link/junction permission limitations explicit.
The beta.13 preparation includes these changes; older assets retain their earlier snapshot.

For the uninstall fix, run the Sandbox-only Test-UninstallRunningApp.ps1 with the
local validation setup. Check All users plus a running tray app, shutdown before
confirmation, checked cleanup and recording preservation. Also test Only me,
late mutex release, access/link failures, native launch error/timeout, Retry/Cancel
and alternate-admin credentials. Verify Ignore on optional cleanup failure proceeds with app removal and keeps remaining settings, while Abort stops removal. The refreshed local packages in `artifacts/packages` include these fixes; historical beta.13 release-folder assets retain their earlier snapshot.

Also verify setup's maintenance radio options for each registered scope and
`/Uninstall` with explicit/default scope. Cancellation must never relaunch the app,
including the elevated setup relay. Test shutdown with visible/tray/audio states,
pending startup, unavailable pipes and older payloads without the capability marker.
Verify the self-contained payload includes `Data/desktop-control.json` on all five
RIDs. Confirm uninstall keeps `%APPDATA%\AthanTime` unless cleanup is checked;
no storage migration is part of this change. Native tests remain Sandbox-only.

## Every change

For cleanup/alignment changes, preserve current packages and both repositories;
check canonical data/media/license references and migrations before removal. Keep
only current release drafts and compact historical evidence. Rebuild/test from
cleared caches, verify actual checkbox centers across eight languages/themes and
wrapped three-state text, inspect English/Arabic captures, and rebuild affected
packages. Remove regenerated staging/build caches after their checks are recorded.

For Windows cleanup access-denied fixes, launch uninstall normally as the desktop
user, choose cleanup, verify that the app exits and only that user's owned settings
are removed, then approve UAC for shared-file removal. Repeat through setup's
Uninstall choice, alternate-admin credentials, UAC cancellation after cleanup,
unchecked/silent removal and an already elevated launch (checkbox disabled/settings
kept). The silent elevated child must never perform user cleanup. Start the native
Sandbox procedure with a genuinely unelevated token; otherwise record a missing
prerequisite rather than claiming this flow passed.

For package-guide changes, check README.txt against packaging/README.txt in the
installed Windows payload and at the root of all five portable ZIPs. Check the
Mac/Linux START-HERE files against source and their executable launchers. Confirm
missing guides fail publishing, regenerate the source ZIP after final docs, and
verify every listed package checksum plus source inventory. Keep the download-folder
README, build information and historical-release distinction aligned.

For the Ubuntu icon fix, verify both Linux ZIPs contain the canonical PNG; run the
shortcut helper as a normal user in an isolated XDG directory, check its installed
icon bytes and Icon/StartupWMClass keys, repeat registration and test a missing
icon. On native Ubuntu GNOME, test menu launch, direct launch after registration,
login startup and replacing a previously pinned generic entry, including XWayland
under a Wayland session. Verify native DEB grouping separately. Do not infer native
dock behavior from the headless/script fixtures.

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
- [ ] Check the actual runtime configuration and bundled CoreLib/coreclr/hostfxr
  in every application package, as well as RID-matching SQLite/audio libraries.
  Verify [download names](PACKAGING.md#release-cleanup-2026-11-11) agree across
  the builder, recipes, native smoke procedures, CI, checksums and GitHub notes.
  Source ZIPs are developer inputs, not self-contained application downloads.
- [ ] Set `Directory.Build.props` version; keep one Release edition per OS.
  Build all six solution projects with the .NET 11 SDK. Verify the shared target
  reaches the app, tests and every tool. Check runtime/OS support and native SQLite,
  miniaudio and graphics libraries on every RID; the native macOS minimum is 15.
  For a beta, verify the translated label in the header/title in all eight
  languages and both themes, and the full prerelease version in About/support
  drafts, setup/uninstall display names and package filenames. Keep upgrade keys,
  bundle/package identifiers and profile paths stable. Check that the chosen
  version upgrades the preceding release and precedes the intended stable release.
- [ ] Verify `Application:SupportEmail` and its fallback both use the approved
  public mailbox `athantimedotnet@gmail.com`, including the actual packaged JSON.
  Check suggestions, complaints and log drafts in all eight languages without
  launching mail clients. Publish the release tag/title/notes and asset checksums;
  mark beta releases as prereleases and keep their installation identities stable.
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
| macOS 15+ Intel x64 and Apple Silicon ARM64 (.NET 11 baseline) | UNVERIFIED |

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
  machine uninstall preserves all profiles/login choices unless the interactive desktop user explicitly selects cleanup; all other users remain untouched. Include
  running-app locks from another user's session for machine updates/removal.
  Continue to check
  locked-file/refused-shutdown rejection, successful graceful running-app update,
  matching-only startup cleanup and preservation of
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
  Test mixed desktop scaling (100%, 125%, 150%, 175%, 200%), negative monitor
  coordinates, a removed monitor and work-area/taskbar changes. Verify the entire
  normal native frame fits after opening, moving monitors, repeated hide/restore
  and returning from maximized/minimized states; those states themselves must
  stay untouched. Check native borders/title bar, desktop vs Retina rendering
  coordinates, tiny-work-area minimum relaxation and later minimum restoration.
  Do not change the real desktop configuration in automated tests.
  On Windows, include actual title-bar pointer drags in both directions across
  mixed-DPI monitors. Verify native move entry/exit, no fitting changes while the
  loop is active, correct hosting-monitor selection, and readable settled header,
  tabs and content. Test hiding/cancelling a move so the gate cannot stay active.
  Programmatic moves and final bounds alone do not cover this regression. Keep
  continuous desktop-capture/GPU behavior distinct from settled window checks.
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
