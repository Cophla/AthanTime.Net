# Code simplicity and resource review

## Uninstall completion - 2026-11-17

Reuse the NSIS success callback for normal removal and successful machine-child
completion. One silent-mode guard avoids duplicate or unattended dialogs; the
parent's zero-exit check excludes cancellation/failure. No application
code, native dependency, background work or new abstraction is needed. macOS/Linux
retain native removal feedback and the unchanged self-contained package matrix.

## Cleanup and checkbox alignment - 2026-11-16

Remove the installer exit-code branch made unreachable by pre-elevation maintenance
selection; install children already skip that page. Retain the user cleanup and
file-access safeguards. Merge the duplicate checkbox styles and use state-triggered
template-part alignment to center the existing Fluent indicator row, including
wrapped captions; no custom control/template or runtime service is added.
Roslyn unused-private checks found no candidates. Data migrations, JSON/XAML/DI
callbacks, offline reference data, native recipes, assets and provenance remain.
Old generated payloads/caches and six superseded release drafts were removed;
compact audit/reproducer history was archived separately from active source.

## Unelevated uninstall cleanup - 2026-11-16

Keep user cleanup in the existing original process and reuse the existing temporary
uninstaller/elevation relay for machine file removal. The silent child skips user
cleanup; an already elevated interactive run disables the optional checkbox. Exclude
the uninstaller's fragile desktop-token branch and move setup elevation after
maintenance choice. Preserve the file-access guard in the authorized removal
process, current-user lease, bounded retries and explicit continuation choices.
No new helper binary, IPC, package or shared profile traversal is needed. Native
UAC and alternate-account validation remains separate from source/recipe tests.

## Package README and freshness - 2026-11-16

One shared plain-text package guide is copied by the existing publish rules.
Portable packaging places the Mac guide beside the app bundle using one explicit
path case; other payloads retain their normal root. Existing archive/publish tests
cover the guide and missing-guide failure across all five targets. Keep OS-specific
startup details in the existing START-HERE files. No new application abstraction,
service, package or resident work is introduced. Freshness uses content hashes and
source inventory; existing release snapshots remain separate.

## Ubuntu dock icon registration - 2026-11-16

Use the existing PNG and shortcut helper: copy one icon into the user's XDG theme,
add the standard icon/window-class desktop keys, and ship the PNG in both Linux
portable ZIPs. Retain Avalonia's existing X11/XWayland assembly class, matching it
in the portable/native entries. No application startup hook, background process,
icon regeneration or dependency is needed. Registration stays opt-in and repeatable;
Windows/macOS payloads and existing profiles are unaffected.

## All-package refresh - 2026-11-16

Reuse the existing InstallerBuilder, verified Windows setup and per-OS layouts.
This task changes package evidence and descriptions, without adding application
logic, dependencies or deployment modes. Keep current artifacts separate from
historical release snapshots and verify every supported CPU's native payload.
Native DEB/DMG and desktop behavior still require matching hosts. The canonical
engineering guide already covers this workflow; no additional agent rules are needed.

## Setup maintenance and explicit shutdown - 2026-11-16

Reuse the existing same-user pipe and Release lease for a bounded pre-host shutdown
command; no new listener, dependency, resident timer or force-kill. Dispatch the exit
on the UI thread after startup, acknowledge before stopping the listener, and wait
for host shutdown to release the lease. The small published capability marker avoids
launching older executables with an unknown argument. Setup delegates Uninstall to
the selected registered copy before elevation; installation children skip maintenance. Windows retains Restart Manager/file guards for legacy or
other-user processes. Unix native quit workflows and all five targets remain.
Preferences stay in the existing special-folder location; a future device-data
split needs explicit migration/cleanup tests. Process tests use isolated identities.

## Uninstall lifecycle fix - 2026-11-16

One NSIS macro now supplies both installer and uninstaller graceful shutdown, retaining selected-file scope and existing guards. Cleanup returns a small shared result enum instead of losing failure causes in a boolean. The existing user-token launcher captures immediate native errors and retains bounded waits/handle cleanup. Optional cleanup has explicit Retry/continue/Abort handling, so cleanup failure cannot trap the user in uninstall. Busy retries are bounded; no new app timer, package or filesystem sweep is introduced. A focused Sandbox procedure exercises the native checkbox and lifecycle separately from pure tests.


## Beta.13 preparation - 2026-11-16

This release preparation changes shared version/release descriptions and rebuilds existing recipes. No application abstraction, dependency, timer or native deployment mode changes. Regional defaults and cleanup retain their separate responsibilities and user-scoped safeguards. Portable-only helper files are reviewed separately from the common setup payload; all five supported architectures remain self-contained.


## Regional defaults and cleanup - 2026-11-15

FirstRunDefaults owns only absent-profile hints, using the existing SQLite repository and injected TZDB. It does not add a runtime network lookup or catalog import. A bounded single-row query handles aliases and region preference. UserProfileCleanup owns only explicit known-file deletion; it does not reuse normal settings loading or create host/media state. Windows launch and cleanup share the existing native user-token function through one NSIS macro. A single Bash consent helper serves Mac and Linux. Separate responsibilities remain separate files; no new packages, timers or broad filesystem deletion are introduced.


## Setup lifecycle - 2026-11-15

2026-11-15 setup lifecycle review: reuse Windows Restart Manager and the bundled NSIS System plug-in instead of a new IPC protocol, helper process or dependency. Shared startup gains only an optional one-time show override. Keep bounded lock polling, native shutdown discrimination, UAC parent handoff, shell-user environment/handle cleanup and legacy failure paths; these are necessary lifecycle/platform safeguards.

## About and beta.12 preparation — 2026-11-15

AboutWindow is kept in AppWindows.cs beside the other secondary windows, reusing
the theme/card base and existing browser/error callback. It owns two underlined
URL buttons and a bound summary. Decorations belong to each window's dispatcher.
The summaries are in the existing eight-language JSON catalog. Information dialogs
retain their distinct confirmation/error role, and portable ZIP creation retains
its separate build responsibility. No dependency or general dialog framework was
added; further file merging would combine unrelated UI and packaging lifecycles.

## Public project links — 2026-11-15

Two public constants provide app project/release URLs. Existing OS URI launching
and shared information windows handle browser navigation and failures. About and
support drafts reuse those constants. Package metadata and documentation record
the approved URLs. This adds no HTTP polling service or package dependency.

## Reminder tests — 2026-11-15

Three UI actions reuse the audio adapter, formatted prayer-alert record and
shared popup events. One preview flag in the existing reading scheduler allows
manual text to expire while automatic settings are disabled/paused. Existing
Reset/Dismiss paths clear that state and the same application clock drives it.
No new service, dependency or timer was added. Reminder history remains confined
to scheduled dispatch.

## Portable launchers — 2026-11-15

One archive writer owns portable layout and ZIP permissions, reusing the native
macOS metadata template. A bounded ZIP central-directory metadata adjustment
sets Unix creator flags because .NET writes the build-host platform and exposes
no creator setter. It edits only the builder's own ZIP and rejects unsupported
Zip64 directory/count sizes explicitly. Small OS-guarded scripts launch the apphost directly
and add an optional Linux menu shortcut or OS-library setup. Script tests use
temporary paths and fake native commands. Settings normalization filters the
removed bundled Alhamdulillah identifier while preserving custom paths.
Dynamic clock/countdown/next-prayer/progress values now use the existing Set
helper, replacing the ordinary-tick tuple/comparisons and covering date-refresh
notifications through the same assignments.

## Interactive monitor dragging — 2026-11-14

The correction keeps geometry in the existing service/window. A Windows-only
observer through Avalonia's existing Win32 hook sets one move/resize flag; it
never handles native messages. Entry pauses fitting and intermediate capture,
exit queues the existing fit, and hide/close clear/unsubscribe the observer.
No new timer, native dependency, service, file or runtime graphics option is added.

The window-hosting monitor replaces top-left-based selection. Removing the
position-event refit and its cached work-area field eliminates a feedback path
while dragging across displays. The portable size/position calculation remains
unchanged. Regression tests call the real callback in the headless UI; a separate
native probe covers actual move loops and rendered header/tabs. Native results
and remaining limits are in [packaging](PACKAGING.md#interactive-monitor-dragging-2026-11-14).

## Window fitting — 2026-11-14

The existing position service now also owns a pure size/position calculation,
using one small shared work-area selection loop. A named tuple carries the
position, client size and effective minimum without introducing another service
or file. The main window retains its XAML minimums, supplies native decoration
measurements and coalesces geometry work on the existing dispatcher. There are
no new runtime dependencies, timers, background workers or profile fields.

Fitting skips hidden/non-normal windows, uses desktop rather than render scaling
and writes no profile during resize/drag. It only changes geometry when needed;
actual changes are debug logged. Position persistence and native-platform limits
remain explicit. Existing lifecycle regressions plus new geometry and real-window
event cases cover the change. See [dated verification](PACKAGING.md#window-fitting-2026-11-14).

## Release cleanup — 2026-11-11

`AppWindows.cs` now groups the shared secondary-window base and its two small
popup implementations, with the same public types/bindings and lifecycle. The
seasonal-rule editor and text settings use expanded setters so clamping, equality
checks, assignment and callbacks are visible in order. Distinct search, audio,
settings, activation and window-position responsibilities remain separate; their
failure/concurrency/platform safeguards have concrete uses.

The redundant portable PowerShell wrapper and its three wrapper-only test cases
are removed. The existing C# builder remains the single portable entry point.
Platform download naming is a direct five-case mapping; native shell recipes
retain only the corresponding OS-specific spelling. Self-contained publish
guards extend the existing target rather than introducing a new framework,
dependency, resident service or packaging abstraction.

Obsolete generated intermediates are removed before clean verification. Required
compressed catalogs, native/media provenance, icons, licenses and regression
procedures remain. This is a targeted readability/packaging cleanup, not evidence
of lower whole-process RAM or an exhaustive proof that every path is minimal.
See [current packaging evidence](PACKAGING.md#release-cleanup-2026-11-11).

## .NET 11 upgrade — 2026-11-10

The framework remains one shared property for all six solution projects. Updated
hosting/SQLite packages use the existing service and data-access code; no new
abstraction, timer, runtime network operation or native integration is needed.
The existing configuration, logging, search, audio decoder, UI and lifecycle
regressions exercise the new runtime. The macOS recipe fixture additionally
checks the supported OS baseline of 15. Native SQLite assets and notices are
reviewed for all five RIDs. See [.NET 11 packaging evidence](PACKAGING.md#net-11-upgrade-2026-11-10)
for actual checks and native limitations; older measurements remain historical.

## Beta.7 support contact — 2026-11-08

Only the shipped contact setting, existing fallback and shared release version
change at runtime. The existing mailto draft path retains its encoding, localized
content and manual-send boundary on Windows, macOS and Linux. No new service,
abstraction, dependency, timer or background work is needed. Tests cover shipped
configuration as well as the fallback so one cannot hide an outdated value in the other.
See [packaging evidence](PACKAGING.md#support-mailbox-beta7-2026-11-08) for this
release; the beta.6 and older reviews below are historical.

## Beta.6 repository preparation — 2026-11-08

The catalog's storage changes to a versioned ZIP with two small shared MSBuild
targets using built-in tasks. Configuration/RID-local extraction prevents build
configurations from sharing output files, and input/output timestamps avoid
repeated decompression. Runtime logic, native dependencies and one-second
reminders do not change. The existing builder creates the archive; no new tool
project, service, runtime loader or general-purpose framework was added.
Real target fixtures and archive/payload hash checks cover failure/incremental
behavior. See [new repository preparation](PACKAGING.md#fresh-repository-preparation-2026-11-08)
for that preparation's checks and native limits; the dated reviews below are historical.

## Beta.5 fixes — 2026-11-05

The beta.4 search and duplicate-volume-save findings below are now addressed in
**1.0.2-beta.5**. UI search uses one owned async worker and one latest pending
query/language, retaining the 250 ms typing debounce and revision checks. A
dispatcher yield publishes worker ownership before fast/reentrant completions;
continuations and result bindings stay on the UI thread. New requests replace
pending work rather than adding thread-pool jobs. Clear/disposal discards pending
work and stale output; the one running SQLite call finishes and releases its
connection normally. Failure logs and releases the worker for queued/later work.

Volume changes still save immediately after clamping to 0–100. Equivalent
assignments now skip writes and notifications. The controlled five-assignment
case changes from five save requests to zero. Controlled blocked-search cases
assert a maximum of one active lookup, latest-only delivery, recovery after a
failure, clear/disposal before and during work, and reentrant UI notifications.
These checks establish bounded work/save behavior, not a whole-app memory budget.

`RefreshDynamicValues` now delegates selected-date display and live tray status
to two named private methods. Today still reuses a single six-event scan; other
selected dates leave the tray live. The existing dated UTC/sunrise, row/cache,
seasonal-draft, language/theme and reminder tests cover that refactor. No new
service, package, thread, timer or file split was introduced. The view model
retains substantial coordination responsibilities; this targeted change does
not claim a complete architectural rewrite or native resource certification.

Fresh verification and the rebuilt installer are recorded in
[packaging](PACKAGING.md#search-and-settings-fixes-2026-11-05). The following
beta.4 audit and older beta.3 snapshot remain historical evidence; their open
search/save findings describe the code before these fixes.

## Historical beta.4 validation — 2026-11-05

This section records the pre-fix beta.4 audit, superseded above for search/save
behavior and display-method organization.

**Qualified result:** the beta.4 code uses mostly direct logic and avoids the
previous full-catalog load and repeated tick allocations, but this review does
not establish that every path is simple or that the full desktop application
uses little RAM/CPU/GPU on every supported machine. The open findings below
prevent a blanket pass. The canonical engineering rules remain in
[AGENTS.md](../AGENTS.md); this report records evidence rather than new rules.

The fresh inventory covers all **67 handwritten C# files**: 29 application,
six tool and 32 test files, with 525 method/constructor/local-function declarations.
Source review covered component responsibilities, tick/search/media hot paths,
cache/task/event lifetimes, storage and OS boundaries, plus XAML/styles, project
references, launchers, native recipes and CI. Generated/vendor implementations
are excluded. Syntax counts locate maintenance hotspots; neither those counts
nor an unused-member analyzer proves simplicity, absence of leaks or low CPU.

### Open findings

| Priority | Evidence | Implication / next validation |
| --- | --- | --- |
| Medium | [MainViewModel.UpdateSearchResults](../AthanTime.Net/ViewModels/MainViewModel.cs) starts a worker for each debounced search or language refresh. The revision check discards obsolete output but does not cancel/coalesce running queries. A headless probe with blocked fake queries observed **two concurrent searches** after two language changes. | Slow storage or repeated changes can overlap workers and SQLite connections/page caches. Coalesce requests to the latest query with bounded concurrency; first add controlled out-of-order, failure and disposal regressions. This is an active-search concern, not proof of an idle leak. |
| Low | [MainViewModel.AudioVolume](../AthanTime.Net/ViewModels/MainViewModel.MediaLocations.cs) saves synchronously on every assignment, including unchanged/clamped values. The in-memory probe observed **five saves for five unchanged-value assignments**. The bound slider can also produce a series of genuine changes. | Redundant assignments perform avoidable primary/backup serialization and writes through [SettingsService.Save](../AthanTime.Net/Services/SettingsService.cs). An equality guard is a small follow-up; changing slider save cadence also needs an explicit final-value/exit/failure contract. The probe counted requests, not disk time. |
| Low | The three main-view-model partials still combine substantial display, settings, search, seasonal-draft and reminder coordination. `RefreshDynamicValues` has 64 lines and `Tick` 73 in this snapshot. | Maintainability remains the largest simplicity concern. Prefer small cohesive private methods when changing these paths, retaining selected-date/live-tray and draft/committed distinctions. File length alone does not justify a new service hierarchy or predict resource use. |
| Unverified | No complete desktop working-set/private-memory, idle CPU, GPU, battery or long-duration native soak measurements were performed in this review. | Do not advertise a full-app resource budget or absence of native leaks. Measure the visible window, tray, search bursts, repeated popups and playback/stop cycles on each matching OS/architecture. |

### Resource and simplicity checks by area

| Area | Source evidence and boundary |
| --- | --- |
| Clock/reminders | One-second app timer; six-event scans reused on Today; value row key; changed-only ordinary binding notifications. Schedules retain at most ten cached dates before clearing; history retains 128 reminder identities. The enabled reminder scan also examines adjacent dates every tick. Accuracy/catch-up safeguards remain necessary. |
| Seasonal rules | Concurrent seasonal-baseline cache clears after its count exceeds 64; period construction/validation is shared. This is a small cache policy, not a hard byte budget under concurrency. Repeated custom-rule evaluation still costs more than automatic rules. |
| Locations | Read-only, nonpooled connections; no runtime archived-catalog import; UI asks for the default 30 results. The configured 2 MiB SQLite page cache is per connection and does not cap total query/process memory. Two-character searches can scan the catalog; overlapping work is the finding above. |
| Audio | Ogg streaming, one player/device ownership path, native work off the UI thread and idle-device cleanup. Device-free decoder tests pass; actual native callback/device lifetimes and repeated playback still require matching-OS soak tests. |
| UI/desktop | Shared secondary-window styles, one retained main window, one current prayer popup and one text popup; the notification close timer is disposed on close. Position writes occur on lifecycle events for changed coordinates. Native graphics caches, Fluent rendering and card shadows are outside the measured tick loop. |
| Persistence/logging | Plain JSON, atomic temporary replacement, validated backup and explicit errors. Saves run for edits/deliveries, not every ordinary tick. Logging rolls by size/date with retained-file limits and has no per-tick log call; exported logs stream to ZIP. Large user-created lists/imports still affect serialization size and time. |
| Startup/activation | Deferred window construction, guarded Release startup before host creation, awaited pipe I/O when idle, bounded duplicate-activation waits and explicit shutdown. These safeguards serve concrete lifecycle/platform requirements; removing them to shorten files is unjustified. |
| Tools/packaging/tests | Catalog generation deliberately processes the full dataset offline; icon rendering, compression, publish and test captures consume build-time resources. They are separate executables/workflows, not resident app services. Platform recipes retain separate native libraries and install/uninstall rules. |

The earlier [beta.4 before/after experiment](PACKAGING.md#idle-resource-reduction-2026-11-04)
measured **54.7–66.1% fewer allocated bytes** and **36.4–44.2% less elapsed time**
in a controlled headless tick loop. Those are historical measurements reused
here, not newly measured whole-app savings. Notifications, native audio, window
rendering, logging and SQLite search were excluded from that experiment.

### Fresh verification and scope

On Windows 10.0.26300 x64 with .NET SDK 10.0.403, both Debug and Release solution
builds passed with zero warnings/errors; **412 tests passed in each**, none
skipped. The offline LocationDataBuilder, outside the solution, also built in
both configurations with zero warnings/errors; no data generation/download ran.
An initial sandboxed solution build could not write Avalonia's external build
log; the normal-cache-access rerun succeeded without source changes.

Fresh read-only IDE0051/IDE0052/IDE0005 analysis had no findings. Five PowerShell
scripts and twelve XML/project/XAML files parsed; five Bash launcher/recipe
syntax checks passed using Git Bash on Windows. Icon output checks and portable
plan dry runs for all five RIDs passed; these did not build native packages.
The two review probes used the real headless view model with fake blocked
location lookups, in-memory settings/audio/login and no running app timer,
native device or real profile. They demonstrate the stated open findings, not
test failures or measured whole-process RAM. Inventory, source hashes and raw
probe evidence are under ignored `artifacts/validation-20261105/`.

The matrix remains `win-x64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`.
macOS/Linux runtime checks and native desktop resource measurements on all three
OSes remain unverified; Windows signing/Application Control remains unresolved.
No production/test code, captions, dependencies, defaults, assets or installer
inputs changed during this validation, so beta.4 and its existing installer
remain unchanged. Existing rules and requirements were reviewed and retained;
no artificial tests, new timer, framework or dependency was added for this audit.

## Historical beta.3 review — 2026-11-04

This is the beta.3 review snapshot. The subsequent
[beta.4 resource update](PACKAGING.md#idle-resource-reduction-2026-11-04) changes the
tick loop and seasonal-period evaluation; the source counts below are historical.

The code is mostly direct, with constructor DI, small services, plain JSON models
and explicit platform branches. The main remaining maintenance hotspot is the
large main view model. A wholesale rewrite, generic service framework or blanket
file merge would obscure working behavior. The canonical rules remain in
[AGENTS.md](../AGENTS.md); this document records this review's findings.

## Scope and method

The inventory covered every handwritten C# file: 29 application, six tool and
32 test files. Before the refactor there were 518 method/constructor/local-function
declarations; afterward there are 519. Property accessors, lambdas and top-level
statements are outside that declaration count. Syntax counts identified review
hotspots; they are not complexity limits or a correctness score.

The component review covered models, service boundaries, DI/startup, desktop
lifetime, view-model state/reminders, UI handlers/styles, installer planning,
data/icon builders, native recipes, signing, launchers and CI configuration.
Test infrastructure was reviewed for isolated fixtures, explicit failure cases,
UI dispatch and matching-OS boundaries; the complete suite was rerun. Generated
outputs and third-party implementations were excluded. Bundled datasets/media
were retained and checked through existing regression/publish checks.

## Findings and action

| Priority | Location | Finding / action |
| --- | --- | --- |
| Addressed | [SingleInstanceGuard.cs](../AthanTime.Net/Services/SingleInstanceGuard.cs), `ListenForActivationAsync` | Pipe I/O contained startup queuing, shutdown rejection and callback error handling. Moved that cohesive dispatch operation into one private `DispatchActivationRequest` method, retaining the same locks, pending flag, error callback and acknowledgement behavior. The loop changed from 59 to 41 lines and six to four nested blocks. |
| Follow-up | [MainViewModel.cs](../AthanTime.Net/ViewModels/MainViewModel.cs), `RefreshDynamicValues` / `Tick` | Selected-date display, current tray status and reminder coordination make this the largest maintenance area. On the next change here, consider named private methods for selected-schedule display and live tray status; keep their different date/UTC behavior explicit. No new service hierarchy is justified by the file's size alone. |
| Follow-up | [MainViewModel.cs](../AthanTime.Net/ViewModels/MainViewModel.cs), `UpdateSearchResults` | `Task.Run` → `ContinueWith` → UI-post makes completion/error flow harder to follow. A later async/await cleanup should retain explicit UI dispatch and first add deterministic out-of-order, failure and post-disposal tests. The existing search regression covers normal lookup, localization and clearing, so a broad concurrency rewrite was left out of this review. |
| Low | [UnixInstallerRecipeTests.cs](../tests/AthanTime.Tests/UnixInstallerRecipeTests.cs) | The recipe fixture combines fake commands and several platform assertions in one long test. Keep the matrix; extract a small fixture helper only when more cases make the setup harder to maintain. Replacing this with a generic test framework would add complexity. |

The refactor adds no public API, service, package, timer or protocol value. Existing
process tests already cover ready/delayed handlers, repeated/concurrent requests,
callback failure/recovery, shutdown, owner restart and Debug/Release policy.
Headless App tests retain hidden/minimized restoration coverage; no test was added
merely to call the new private helper.

## Deliberately retained

| Area | Why the existing detail is needed |
| --- | --- |
| Settings persistence | Atomic replacement, validated backup, future-schema refusal, migrations and sticky warnings have different failure contracts; combining them into a generic serializer would hide those rules. |
| Time zones/calculation | Noda Time overlap/gap and recurring-season rules, original UTC instants and the Adhan adapter preserve documented accuracy behavior. No duplicated astronomy was introduced. |
| Audio | Native operations remain off the UI thread with explicit device/provider ownership, generation checks and streamed decoding. The recently consolidated file remains cohesive. |
| Desktop/IPC | The pre-host mutex and activation channel have different lifetimes. The owning thread, same-user isolation, deadlines and UI-post callback remain explicit. |
| UI | Shared styles and `AppWindow` already centralize appearance. Stable selectors/rows, detached summer-time drafts and explicit native picker handlers remain intact. |
| Data/tools | SQL parameters, bounded read-only connections, Unicode normalization shared with the builder, strict icon inputs and checksum-verified NSIS bootstrapping serve concrete requirements. |
| Packaging/signing | Per-OS recipes, scope separation, elevation, exact uninstall payloads, stable identities and native/signing checks cannot safely be replaced by one universal branch. |
| Tests | Serialized headless sessions and temporary profiles/fake OS integrations prevent real-device/profile changes. Large test files mostly contain independent scenario matrices, not shared production algorithms. |

## Verification and limits

Windows 10.0.26300 x64, .NET SDK 10.0.403: Debug and Release builds passed with zero
warnings/errors, and **406 tests passed in each configuration**, none skipped.
Five PowerShell packaging/test scripts and 12 XAML/project/build XML files parsed.
Read-only IDE0051/IDE0052/IDE0005 analysis completed without findings, and 101 local
documentation links/anchors plus Git visibility/diff checks passed.
The version remains `1.0.2-beta.3` because the refactor preserves behavior/protocol.
Inventory snapshots are under ignored `artifacts/simplicity-review-20261104/`.

Windows/macOS/Linux were considered against the unchanged `win-x64`, `linux-x64`,
`linux-arm64`, `osx-x64` and `osx-arm64` matrix. No dependencies, translations,
settings defaults, licenses, data, assets or installer/uninstaller behavior changed.
This source review is not native desktop certification. Windows signing/Application
Control remains unresolved; macOS/Linux execution and native desktop integration
remain unverified. Current installer evidence belongs in
[packaging](PACKAGING.md#repository-wide-simplicity-review-2026-11-04).

Beta.14 preparation changes release identity and evidence only; existing runtime and packaging safeguards remain. No new dependencies or abstractions.
