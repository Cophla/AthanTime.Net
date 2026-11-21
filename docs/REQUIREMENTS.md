# Product requirements and decisions

This is the product contract; [AGENTS.md](../AGENTS.md) owns shared AI engineering rules.
Every change follows its [change completion rule](../AGENTS.md#change-completion-rule)
for affected tests, comments, descriptions, rules and supporting files.
Implemented features below are not native-OS certification. See dated results/limits
in [packaging](PACKAGING.md) and the [release checklist](RELEASE-CHECKLIST.md).

## Scope

A simple, accurate, offline-first Avalonia/.NET 11 desktop app for Windows, Ubuntu/Linux
and macOS. Runtime needs no network, account, API key or paid service. Prefer trusted,
free/open-source, business-usable .NET dependencies and keep logic understandable.
Do not promise every IslamicFinder feature or every settlement without verified coverage.
Prefer direct code and existing helpers; retain the failure handling and desktop
lifecycle needed for reliable offline operation. The engineering
[simplicity/change rules](../AGENTS.md#working-rules) apply to every change.

Windows, macOS and Linux capability is a product-wide requirement. Features and
deployment choices must preserve the declared platform/architecture matrix, with
platform-specific differences and limitations made explicit. The mandatory
engineering and verification rules are in [AGENTS.md](../AGENTS.md#working-rules).

The shared .NET 11 target applies to the application, tests, single-instance probe,
installer builder, icon builder and location-data builder. Self-contained downloads
include .NET; developers need the .NET 11 SDK. Keep Windows x64, Linux x64/ARM64
and macOS Intel/Apple Silicon targets. .NET 11 raises the supported macOS baseline
to 15; Ubuntu 22.04/24.04/26.04 and Debian 13 remain declared Linux desktop baselines.
Native desktop verification remains distinct from builds and package inspection.

Release descriptions use plain language for everyday users. Describe useful changes,
how to start the app on Windows/macOS/Linux and essential beta/security caveats;
keep internal build reports, local paths and personal feedback out of the description.
Required licenses and provenance remain available separately. Publication and commit
confirmation follow [AGENTS.md](../AGENTS.md#working-rules). Documentation edits must
preserve existing published versions and binaries unless their exact change is confirmed.

## Required behavior

- First launch per OS user (not installer/admin culture): use the OS UI language
  when supported, otherwise English; initialize 12/24-hour display from regional
  time format. Use injected embedded TZDB to map the OS zone and its aliases;
  suggest one catalog city in that zone, preferring region then population/stable ID.
  Region/language/time cannot identify the exact city. A visible localized review
  asks the user to confirm the displayed city or choose Settings > Location.
  Automatic prayer reminders wait for confirmation; confirmation/selection never
  replays earlier reminders. Missing/unknown zone or no match retains the visible
  fallback city for review. Existing, recovered, malformed or future-schema profiles
  never receive regional reinitialization. No network/geolocation permission.

- System date/time changes must refresh the next-Athan countdown, next prayer/time and progress with property-specific notifications as well as refreshing displayed prayer rows. Preserve automatic TZDB conversion at each prayer instant.
- Schedule display: five prayers (Fajr, Dhuhr, Asr, Maghrib, Isha), Shoroq/sunrise,
  next prayer, UTC-based countdown, selected-date navigation, Islamic midnight and
  qibla. Today is primary; all six times
  fit the default 1180×800 window. Center first startup; keep 980×600 scrollable/readable.
- Settings opens on General, then Location, Prayer & clock, Audio & Adhkar and Movement breaks. Light/dark themes,
  readable buttons/numbers, visibly bordered tabs with distinct filled selection
  and keyboard focus, Help/About/version and one confirmed exit path.
  App-owned confirmations, information dialogs, prayer popups and text-Adhkar
  windows share the main window's palette, typography, card surfaces and control
  sizing. Follow selected theme and RTL/LTR direction while open. Keep text-Adhkar and movement-break
  windows non-activating. OS file pickers retain the platform's native appearance.
  Center the city search placeholder and typed text vertically with space on both sides.
  Action buttons share the main window's green palette, rounded bordered shape,
  readable labels and hover/pressed/disabled states, with visible keyboard focus.
- Public links: use https://github.com/Cophla/AthanTime.Net for the project and https://github.com/Cophla/AthanTime.Net/releases for downloads, including betas. General settings opens these through the OS browser on explicit user action. About provides a concise localized feature summary and explicitly clickable, visibly underlined project/download URLs. About renders the version in a separate LTR block so RTL text cannot reorder it. About, support drafts and package metadata carry the same approved links. Automatic online version checking remains a proposed optional feature.
- Release identity: `Directory.Build.props` owns the semantic version. Current
  source preparation is **1.0.2-beta.15**. Beta builds show a translated Beta label in the
  main header/window title in every language/theme. About, support drafts and
  package/setup display names retain the prerelease version; hide source-revision
  metadata in user-facing version strings. Keep install/profile/login/single-instance
  identities stable so release channels reuse the existing update/settings flow.
- Calculation: Adhan astronomy, Sunni presets, Standard/Hanafi Asr, high-latitude rules.
  Country conventions select only visible method/Asr/high-latitude options; manual
  choice stays available. No hidden corrections or timetable substitution.
- Corrections: explicit ±60 minutes for each of the six rows, stored per stable
  case-insensitive location ID. `MinuteAdjustments` is the active copy;
  `LocationMinuteAdjustments` retains saved cities. Schema 5 migrates old values only
  to the previously selected city. New cities start at zero unless already saved.
  Hijri day correction remains global (±2).
- Offline locations: bundled GeoNames `cities500`, country/place search, coordinates,
  population ranking and translated alternate names where available. Also allow
  saved manual coordinates with a validated IANA zone and optional country code.
  Use a prebuilt read-only SQLite catalog with bounded query memory; do not load all
  places or build the search index at startup. Existing prayer settings and transfers remain JSON;
  movement-break settings and instructions use their separate per-user SQLite store.
  Source checkouts version the prebuilt catalog as `locations.db.zip`; MSBuild
  extracts it incrementally under configuration/RID-local `obj` and copies it as
  `Data/locations.db` to build/publish outputs on all supported platforms. No
  runtime extraction/download, Git LFS or external unzip utility is required.
  Debounce typed searches by 250 ms. Run at most one UI search query at a time,
  retaining only the latest pending text/language. Discard stale results and pending
  work on clear/disposal; allow a running query to finish/release its connection.
  Search failures must not prevent later requests. Apply results on the UI thread.
- Languages: `en ar fr es tr de id ur`. Arabic/Urdu are RTL, others LTR; numeric
  inputs stay LTR. All captions use the single embedded `Data/ui-text.json`
  catalog with the same keys in all eight languages. Language/key matching is
  case-insensitive; unknown languages use English and unknown keys remain visible.
  Switching language/theme preserves selections, prayer rows, values and drafts.
- Prayer reminders: independent before/at/after settings for every row, including
  sunrise; before/after durations 1–180 minutes. Before-prayer popups default to
  15 minutes and are enabled for the five prayers; sunrise stays opt-in. Users
  can choose 30 minutes or another supported value per row. Preserve saved choices.
  Notification settings must clearly separate before, exactly at the displayed
  prayer time, and after. Each has an independent enable switch; before/after
  minute values persist even while disabled. After-prayer defaults to 5 minutes,
  disabled. Disabling one moment must not suppress the other enabled moments.
  Shoroq/sunrise is a time marker, not a prayer: its row captions, timing description
  and before/at/after popup text must use sunrise wording in all eight languages.
  Keep its time, optional reminders, audio and saved settings unchanged.
  Sunrise does not become the current/next prayer or the target of a prayer countdown.
  Per-row local Ogg Vorbis audio at
  prayer time, master switches, volume 0–100 and simple play/stop controls.
  Save actual clamped volume changes immediately; assigning the same effective
  volume must not rewrite settings or notify unchanged bindings.
- Manual tests: every Audio & Adhkar tab has a localized Test button using shared
  styles. Prayer test uses the next actual prayer popup and its configured audio
  switches/path. Recording test uses selection, first playlist item or Takbeer.
  Reading test uses selection, first saved text or current default and expires
  after the configured duration even while automatic reminders are disabled/paused.
  Tests preserve saved switches and prayer delivery history. Reading tests do not
  advance automatic rotation and dismissal starts a full pause. Reuse the existing
  audio adapter, themed popups and app clock on Windows/macOS/Linux.
- Adhkar audio: multiple local recordings, sequential or random (repeats allowed),
  intervals 1–1440 minutes. Prayer audio takes priority; busy/missed intervals are
  skipped, never queued. Keep decoding streamed and release idle audio resources.
- Text Adhkar: sourced Arabic morning/evening/general defaults with meanings in all
  eight languages, editable multiline custom list, display 5–600 seconds then close
  and wait 1–1440 minutes. Reuse the app clock; no catch-up windows. Source/scheduling
  caveats and default audio provenance are in [audio help](AUDIO.md).
- Desktop: tray prayer/countdown status, show/exit/play/stop/category controls and
  global pause 1–1440 minutes in app/tray. Pause stops current audio/text immediately.
  Close-to-tray is not exit; missing tray must leave the app accessible. Login startup
  is opt-in and current-user only on each OS.
  Login/Start minimized applies only at initial opening. Tray Show/click restores
  and activates the existing window, which must stay visible until the user hides
  or closes it. Repeated hide/restore cycles must work without changing saved
  startup preferences. Without a usable tray, retain the minimized taskbar route.
  Remember the main window's last normal position per OS user in device-local
  `AthanTime/window-position.json`. Save on hide, close or deactivation, without
  writing for every drag event or unchanged coordinates. Restore across launches
  and tray cycles, preserving the existing default size/start-minimized behavior.
  Minimized, maximized and fullscreen coordinates must not replace the last normal
  position. Fit both window size and position to the current display's work area
  on opening, tray restoration and after monitor/DPI/resize/state changes settle.
  Reserve space for native frame decorations and use desktop scaling independently
  of rendering density. Hidden/minimized/maximized/fullscreen windows are not fitted.
  Select the monitor hosting the native window rather than the monitor containing
  only its top-left corner when it spans displays. Ordinary position events must
  not refit the window. On Windows, leave native move/resize and DPI messages
  unhandled, suppress fitting/intermediate capture while that loop is active, and
  queue fitting after it ends. Hiding/closing clears the gate. Verify real drag
  loops and rendered captions as well as settled programmatic bounds; other OS
  native drag timing must be checked separately.
  Keep the preferred 980×600 minimum where possible; temporarily lower effective
  minimums on smaller work areas and restore them when more space is available.
  Such displays can fall below the readable layout baseline. Size is not persisted;
  missing/offscreen monitors fall back to the primary display, with the title bar
  reachable even on a smaller work area. Missing/damaged position files use
  centered startup; read must not rewrite them. Storage failures log and leave
  the window usable, with a later save retry. Position is separate from settings
  transfers and survives app updates/uninstall unless explicit current-user cleanup is chosen.
  Absolute placement and size depend on the OS window manager/compositor; Linux retains
  the existing X11/XWayland deployment boundary. Native checks are required.
  Allow one running **Release** instance per OS user across sessions on Windows,
  macOS and Linux. Manual and login Release launches share the same guard
  regardless of executable location. **Debug development builds** bypass the
  guard, allowing concurrent Debug instances and coexistence with Release.
  This is a build-time exception, not a runtime environment/logging preference;
  Development environment variables cannot disable Release enforcement.
  Concurrent Debug runs share the existing per-user settings/logs and run their
  own reminder/audio schedules; they do not coordinate writes or dispatch.
  A duplicate Release launch requests the existing main window to show, restore
  from minimized/tray operation and activate, then exits before initializing
  its own settings, logs, audio, reminders or windows. Use a local same-user
  channel, bounded waits and no executable-path-specific identity. Coalesce
  requests during primary startup until its first Opened/start-minimized handling
  completes; dispatch showing on the UI thread. Debug launches bypass activation
  as well as the guard. An unavailable/denied channel or an older primary without
  activation support must not create a second host; the duplicate exits with
  status 1 (0 for an accepted request). Foreground focus remains subject to OS
  policy, elevation and the primary instance's desktop/session. Keep ownership
  while minimized/in the tray and until host
  shutdown completes. Normal exit or forced termination permits a fresh launch.
  When minimized startup uses the taskbar instead of hiding to the tray, show the
  window without requesting focus and minimize it once after the platform shows it.
  Handle macOS shell/Dock `Reopen` activation through the same UI path, including
  when Launch Services reuses the process. Keep ordinary background/file/URI
  activation separate; reopening must not apply settings or dispatch reminders.
  Tray, title-bar and taskbar icons use size-appropriate versions of the same green
  crescent/minaret motif; tiny details must not fragment at small display sizes.
- Diagnostics/support: optional per-user `AthanTime/Logs`; disabling logging closes
  the sink and deletes only app-owned logs, removing the directory only when empty.
  Cleanup failures stay visible. Debug verbosity changes at runtime, not via a
  separate user edition. Localized email drafts use the user's mail app and the
  public project mailbox `athantimedotnet@gmail.com` for suggestions, complaints
  and log sharing. Shipped configuration and its missing-setting fallback must agree.
  Log ZIPs require review/manual attachment. Never send automatically or export
  settings as logs.
- Packaging: one self-contained Release edition per OS, free native recipes behind
  `tools/InstallerBuilder`, plus portable ZIPs. Root launchers offer one-click
  native packaging on Windows/macOS and terminal launch on Linux, use the shared
  version, retain errors/results, and never install or upload the output. See
  [packaging setup](PACKAGING.md#one-click-launchers) for prerequisites.
  Portable macOS packages include a Finder-launchable .app and a .command helper;
  portable Linux includes detached launch, menu shortcut and Ubuntu/Debian
  OS-dependency scripts. Preserve executable ZIP modes on Windows builds.
  Self-contained deployment is the distribution default so first app launch does
  not depend on separately installing .NET. Keep the shared C# codebase and current
  SoundFlow/NVorbis audio approach with the required native payload for each RID.
  Framework-dependent packages and a custom universal installer are deferred;
  future alternatives must cover missing-runtime handling on every target OS.
  [Deployment policy](PACKAGING.md#deployment-policy) records the adopted direction.
  Direct recipes also use the shared version/current-host CPU when omitted;
  explicit overrides remain available. Native macOS metadata requires three
  numeric base-version components; macOS display metadata/DMG names retain the
  full release label. Debian package versions translate the prerelease separator
  to `~`, ordering the beta before its final version. Package filenames and
  portable archives retain the shared semantic version. Download filenames spell
  out Windows/Linux/macOS and CPU, distinguish Setup from Portable, and use
  GitHub-compatible ASCII characters. macOS names distinguish Intel from Apple
  Silicon. These names never change install, profile or single-instance identities.
  Publish guards reject framework-dependent packages, missing bundled runtime
  libraries and deployment modes that have not received native validation.
  Reject invalid versions and mismatched native hosts before publishing.
  Windows packaging detects installed NSIS or downloads a pinned, SHA-256-verified
  portable build tool into ignored artifacts; cached tools work offline and never
  become an application runtime dependency.
  Optional Windows build-time Authenticode signing uses an existing trusted
  provider certificate and Windows SDK tools. Cover build helpers, managed/native
  payload binaries (including portable Windows ZIP contents), the embedded
  uninstaller and final installer. Windows signing must not target Linux/macOS
  RID payloads. Preserve valid
  vendor signatures and fail requested signed builds on sign/verify errors;
  never change Windows security policy or add a runtime signing dependency.
  See [Windows signing](WINDOWS-SIGNING.md) for prerequisites and verification limits.
  Support fresh installation and updates while retaining the user's profile and
  recordings. Windows setup offers **Only me** (default, no elevation) and
  **All users** (Program Files, administrator permission for setup/update/removal).
  Always show the Windows destination page: fresh installs prefill the selected
  scope's default folder and allow browsing; updates display the retained folder
  read-only with browsing disabled. Going Back to a fresh scope restores editing.
  Defaults/custom destinations must remain populated when their directories do
  not exist yet. Honor native `/D=` overrides, including spaces; normalization
  failure must not replace the selected path with an empty string.
  Use separate current-user and machine registration/shortcuts. Updates retain
  their registered scope/folder; when both copies exist, automatic selection
  prefers the current user's copy and explicit scope switches select the other.
  Different scopes may coexist in different folders, never overwrite each other.
  Settings, recordings and opt-in login startup remain individual user choices.
  Windows setup reuses the registered folder, including legacy custom
  folders. At committed Install, request graceful exit of only this installation
  through a bounded same-user shutdown request when the installed capability marker
  is present, then selected-executable Restart Manager as fallback. Explicit IPC
  exit and native Windows shutdown bypass tray hiding and exit
  confirmation, allowing normal host/media cleanup. Never force-kill. Retain
  bounded write-access polling and Retry/Cancel for older/refusing apps. Do not
  allow skipping locked payload files. Successful interactive setup opens the app
  visibly without changing minimized/login preferences; silent setup does not
  launch. Read the elevated child's final destination and launch as the original
  desktop user, never the administrator profile. If safe launch is unavailable,
  leave the installation intact and show the Start-menu fallback.
  Updating preserves login-startup registration; per-user uninstall removes that
  entry only when it points to the removed installation. Machine uninstall leaves
  all per-user login entries untouched by default; explicit interactive cleanup may remove only the desktop-user entry. Other users should disable startup before removal.
  The installer-owned directory marker retains uninstall scope independently of
  shared registration; legacy missing markers mean current-user installation.
  Windows uninstall removes the
  known bundled payload, retaining user-added files and the separate profile.
  Windows uninstall requests graceful exit at startup in the original user and again
  before cleanup/removal, reusing the same-user pipe/selected-executable Restart Manager path. Never
  force-kill; retain the write-access guard and manual fallback for older/refusing apps.
  Windows uninstall offers an unchecked Remove my settings checkbox in its original
  unelevated user process. After optional cleanup, only All users file/registration
  removal requests UAC in a silent child. An already elevated launch disables the
  checkbox and keeps settings; direct the user to start uninstall normally. Setup
  maintenance selection also precedes elevation. User cleanup precedes the later
  UAC prompt and cannot be rolled back if that prompt is cancelled. Silent uninstall
  keeps settings. Never launch token-based cleanup from an elevated uninstaller.
  Remove known settings/backups/temp/position files and app-owned logs; disable that
  user login entry, retain recordings/unknown files and never traverse linked profile
  or log paths. Retry a busy instance lease for a bounded interval after shutdown.
  Failure exposes native launch stage/Windows errors or specific app cleanup results
  with Retry, Ignore (keep remaining settings and continue removal), or Abort. Abort stops uninstall before payload removal. The pre-host cleanup
  mode acquires the per-user lease; it refuses a running Release app and never starts
  a host or sends activation. Unix portable/DMG helpers require typed consent and
  reject root. Run before removing the app; DEB removal never sweeps user homes.
  Windows portable includes a current-user confirmation helper too. Other users are
  always untouched. Close concurrent Debug runs manually before cleanup.
  A stale Windows uninstaller must preserve shared registration and shortcuts
  belonging to a different installation.
  Setup offers Install/update and Uninstall when the selected scope is registered;
  `/Uninstall` delegates to that registered uninstaller. Maintenance completion or
  cancellation must not trigger installation's automatic launch. Missing/unusable
  registered uninstallers require repair first. Older apps without the shutdown
  marker retain the manual-exit fallback; Debug runs must be closed manually.
  Successful interactive Windows uninstall displays an explicit completion message
  once, including All users removal through its silent elevated child. The original
  interactive parent displays that child's success only after exit code zero;
  cancellation/failure and silent uninstall must not show a success message.
  macOS/Linux removal continues to use Finder/the package manager or manual portable
  removal; their native tools provide completion feedback.
  Linux updates use the same DEB identity; macOS replaces the same application
  bundle. Exit the app before updates and retain the installation path for login
  startup. See [installation and updates](PACKAGING.md#installation-and-updates).
  Optional five-RID public-repository
  CI is build infrastructure, not a runtime dependency or installation test.

## Linux desktop registration

Checkbox captions and indicators share the same vertical center in normal/taller
and wrapped rows, in every language/theme and RTL/LTR layout. Shared styles retain
Fluent's native checked/unchecked/indeterminate and keyboard behavior. Avoid fixed
caption offsets that depend on a particular font or OS.

Every distribution includes a plain-text user guide for starting, updating and
removing the app, per-user settings, support and beta limits. Portable ZIPs expose
README.txt at their root; macOS places it beside the bundle. Mac/Linux retain
START-HERE.txt with their native startup instructions. Guides state that .NET is
bundled, explain Linux icon registration and never require a terminal to stay open.

Linux portable application-menu registration is opt-in through Install-Shortcut.sh.
It installs the exported icon under the user's XDG data directory and writes an
icon name plus the matching Avalonia X11/XWayland window class, so Ubuntu can group
the running app with its dock/menu entry. Native DEB uses the same desktop metadata.
The helper is repeatable and supports folders containing spaces/metacharacters.
Moving/updating the portable folder requires rerunning the helper; stale pinned
generic shortcuts may need to be replaced. Native desktop validation remains required.

## User-data location

User preferences use .NET `ApplicationData/AthanTime`, which resolves to
`%APPDATA%\AthanTime` on Windows. This is a valid per-user roaming-preference
location, separate from Program Files and other users. Logs and window placement
currently share that legacy folder; Microsoft's guidance favors LocalApplicationData
for device-specific data. No storage migration is included in the uninstall fix.
See [the Windows storage review](PACKAGING.md#windows-maintenance-and-storage-review-2026-11-16).

## Accuracy and summer time

Inputs are coordinates, local civil date, IANA zone, calculation preset, Asr school,
high-latitude rule, explicit prayer corrections and seasonal-clock preference.
Adhan owns solar equations and qibla; the adapter must not change them to match a
legacy program. See the dated [Sidon comparison](ACCURACY-COMPARISON.md).
Calculated schedules/regional defaults are not official observed mosque timetables.

Noda Time's embedded TZDB supplies historical/recurring civil rules for each city,
not OS rules, country-wide offsets or static annual dates. Convert at each event's
instant; retain original UTC instants for elapsed countdowns, reminders and night
midpoint. Automatic summer time is default. Negative-savings normalization is
display/manual-only and must never change automatic civil clocks.

Unexpected legacy/unknown zones must be logged and fall back visibly and
deterministically to UTC rather than crash. Manual coordinate input rejects invalid
zone IDs before saving; import validation must not silently accept an unknown location.

`SummerTimeOverrides` is per-zone and separate from prayer corrections. Modes are
automatic, custom annual dates, manual on/off (±180 minutes). Annual Gregorian
rules choose a month and calendar day or first/second/third/fourth/last weekday,
plus local pre-change hour/minute. Include start/exclude end; allow cross-New-Year
seasons; unavailable calendar days use month end. Today uses actual seasonal
status, other selected dates noon. Future legal changes need an updated offline release.

Summer-time editing is a detached preview. Only Apply commits/recalculates;
Cancel/location change discards edits; Restore automatic still requires Apply.
Language/theme changes preserve the draft. Show committed clock status separately.
Invalid drafts cannot replace active rules; invalid loaded seasons stay visibly
inactive, never an automatic fallback. Retain old forced-state preferences when
`UseCustomSchedule` is absent. Failed writes stay visible and can be retried.

## Movement breaks

Beta.16 adds Settings > Movement breaks, using shared theme/button styles and
all eight UI languages. It is opt-in and disabled initially. The user chooses a
full pause between breaks (default 30 minutes, range 1–1440) and popup duration
(default 20 seconds, range 5–600). Closing/expiry/Done starts a full pause;
Snooze waits five minutes. Test works without enabling automatic reminders and
must not advance their idea rotation; snoozing a test never enables automation.
Restarting the app starts a fresh full pause rather than resuming an old deadline.

Use the existing one-second app clock. Prayer popups/audio and text Adhkar take
priority; skip occupied intervals instead of queuing catch-up notifications.
Sleep, backward or greater-than-five-second clock gaps, global pause/resume,
settings import and disposal reset the interval and close the movement popup.
Timing requires an awake, running, responsive app; it is not a wake-up alarm or
medical guarantee. Suggestions are gentle, original prompts with a visible
comfort/safety note, not a prescribed treatment routine.

Keep practice text, per-language translations and movement settings in writable
`movement-reminders.db` under the current OS user's app-data folder, separate
from the read-only location catalog. Seed eight original ideas in all eight
languages only when creating a database; an intentionally empty saved list stays
empty. Bound the list to 100 ideas and each instruction to 1,000 characters per
language. Cache the bounded list in memory, use short-lived connections on load or
edit, and perform no database queries, extra timers or network calls on ticks.
Transactionally upgrade the early development database shape that lacks stored
translation order, retaining its settings, custom text and original insertion
order. Missing optional journal/WAL/shared-memory sidecars are normal and must not
be detected by deliberately throwing file-not-found exceptions.

The editor adds/removes ideas and saves manually entered translations for a chosen
language. Missing translations visibly fall back to English or the first available
original in its stable saved order; arbitrary custom text is not translated
automatically offline. Shared UI captions remain in `Data/ui-text.json`.

Keep a dedicated movement transfer alongside the whole-app backup. The standalone
portable movement JSON contains enabled state, interval, popup duration and every
idea/translation in stable order; it does not contain runtime deadlines/history,
prayer settings, audio, logs or the SQLite file itself. Export through an atomic
same-directory temporary file and refuse active database/sidecar destinations.
Import reads and validates a bounded supported-schema snapshot before showing an
explicit replacement confirmation. Reject malformed, future-schema, duplicate,
unsupported-language or out-of-range content without changing saved or active
state. Cancel also changes nothing. Confirmed import transactionally replaces only
movement preferences and ideas, closes an active movement popup and starts a fresh
full interval. Ordinary prayer-settings import retains the saved movement data but
also resets its runtime interval/popup. Explain both transfer boundaries in the UI.

Normal uninstall keeps the database; explicit current-user settings cleanup removes
only its known file/SQLite sidecars, preserving other users, recordings and unknown
files.
See [movement-break help and design references](MOVEMENT-BREAKS.md).

## JSON persistence and transfer

- Settings > General offers Restore factory settings. Ask for explicit
  confirmation before clearing the current OS user's settings and backup,
  window placement, movement data, app logs and login startup registration.
  Keep user recordings, exported backups and unrelated files. On success exit
  without writing placement or logs back. The next launch uses ordinary first-run
  rules, regional hints and bundled defaults. Report a failure in the app.
  Missing profile, backup and placement files are expected on first run.
  Check for their absence before attempting to read them.

- Provide one backup panel under Settings > General. The user selects All
  settings or a top-level section and uses the same Export and Import buttons.
  Audio & Adhkar covers its subsections and accepts their older backup files.
  Use a versioned, section-tagged file and validate before confirmation. The
  All settings option includes both the portable JSON preferences and movement
  SQLite preferences/ideas. Legacy JSON imports retain the existing movement
  data. Apply only the selected scope. Device login registration, logging
  choices and reminder history stay local. Backups do not contain audio or logs.
- New text Adhkar settings show readings for five seconds. Preserve saved display
  durations in both features. Settings > General controls the shared reading
  highlight speed for new popups from 2 to 20 characters per second, initially 8.
  Advance by grapheme, following the string direction and skipping spaces. Include
  the speed in General and whole-app backups and preserve it when importing an older
  backup without the field. The popup's
  own short-lived timer controls the highlight and the existing app clock controls
  dismissal and reminder timing.

- Existing configuration, defaults, conventions, UI translations and prayer settings use JSON.
  The large location catalog is shipped as read-only SQLite generated from archived JSON.
  Movement instructions, their translations and movement preferences are stored in
  per-user SQLite at runtime and use their own bounded portable JSON transfer; they
  are included in the whole-app transfer and can also be backed up separately.
  Development/Release files are developer environment defaults, not different user editions.
- Write through a same-directory temporary file/rename and keep a validated
  `settings.json.bak`. Recovery Load does not rewrite the damaged primary; a later
  successful Save can replace it. A newer schema must not be silently downgraded.
- Profile recovery/load/save warnings take priority over transfer outcomes and remain
  visible on every page; successful export or cancelled import cannot hide them.
- Import reads/validates without applying, requires a resolvable selected location
  and user confirmation. Reject unrelated/malformed/future-schema JSON. Preserve
  device `StartOnLogin`, `LoggingEnabled`, `DebugLoggingEnabled` and
  `RecentPrayerAlerts`; clear imported pause/media selections/unapplied drafts,
  reset interval clocks, stop media and clear calculation caches before refresh.
- Export cannot overwrite the active profile/backup/temp paths. JSON contains saved
  coordinates and local audio paths, not recordings or log files; disclose/review
  before sharing. Copy external recordings separately.

## Reminder lifecycle

Persist the latest 128 case-insensitive, invariant Gregorian UTC alert identities
before dispatch; do not clear history at midnight. Within retained history, restart
or clock reversal must not duplicate notifications/audio. Never fire before exact UTC.

A backward or >5-second timer gap closes stale text and starts fresh Adhkar intervals.
Skip due events older than resume UTC, including earlier events in the same minute;
an event exactly at resume may fire. Ordinary first startup may deliver an unrecorded,
already-due event within the current UTC minute. Paused reminders are not replayed.
Schedules operate only while the app is running/awake; they are not OS alarms.

## Coverage and release limits

- Bundled data currently covers 235,844 populated places in 252 countries/territories.
  GeoNames `cities500` covers population >500 plus administrative seats, not all
  settlements/features. Alternate names are not available for every place/language.
- Convention resolution is city → country → documented global MWL fallback.
  Require dated authoritative evidence for mapping changes; never invent official
  city timetables or minute offsets.
- All eight languages, both themes, stable selectors, RTL geometry, minimum/default
  sizing, JSON migration/recovery/failure/import, summer rules/UTC durations and
  restart/sleep/pause behavior need regression coverage.
- Use pure xUnit.net v3 with constructor DI, Microsoft.Testing.Platform and
  Avalonia.Headless. Fake native boundaries and temporary profiles; never mutate
  real startup registrations or the user's settings/logs in automated tests.
- Actual setup/DEB/DMG installation, tray, sound, file pickers, email and login tests
  remain target-OS smoke checks. Windows 10 is best effort, not blanket vendor support;
  record exact OS/architecture/version evidence. CI/cross-publishing does not certify it.
- Offline data/license/translation update procedure and unverified native matrix:
  [RELEASE-CHECKLIST.md](RELEASE-CHECKLIST.md). No runtime auto-updater/downloads.
