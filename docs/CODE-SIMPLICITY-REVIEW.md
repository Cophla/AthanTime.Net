# Code simplicity and resource review

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
