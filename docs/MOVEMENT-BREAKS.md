# Movement breaks

Movement breaks are included in the beta.16 candidate.
[Requirements](REQUIREMENTS.md#movement-breaks) own
the behavior contract; [AGENTS.md](../AGENTS.md) owns engineering rules.

## Using reminders

Open Settings > Movement breaks. Reminders are off until enabled.
Choose the pause between breaks (30 minutes initially) and how long the reminder
stays visible (20 seconds for new profiles). After closing a reminder or letting it expire,
the full pause starts again. Done closes it; Snooze waits five minutes. Test lets
you try an idea without enabling automatic reminders or changing their rotation.
Restarting the app starts a new full pause. Testing or snoozing a test while
automatic reminders are off never turns them on.

A colored character advances through the text in its reading direction, at
eight characters per second by default. Change the pace in Settings > General.
Increase the display duration
for instructions that need more reading time. Saved durations are retained.

The ideas include simple prompts to stand, walk if comfortable, rest the eyes,
relax shoulders or gently change posture. Move only as comfortably as you can;
stop if you feel pain or dizziness. This is a reminder, not a diagnosis, treatment
or substitute for individual professional advice. The popup duration is not a
prescribed exercise or sufficient-break duration.

Reminders use the app's existing one-second clock while it is running and the
computer is awake. Prayer popups/audio and text Adhkar have priority. Occupied
intervals are skipped; missed reminders are not replayed after sleep. Clock gaps,
global pause/resume and import reset the interval and close an active reminder.

## Editing and saving ideas

Select an idea to edit its instruction, or add/remove your own ideas. Choose the
instruction language to enter a translation yourself. The defaults include
English, Arabic, French, Spanish, Turkish, German, Indonesian and Urdu. Missing
translations use English or the available original text. No online translation
service or translation model is used. Save up to 100 ideas, with up to 1,000
characters in each language's instruction. An empty list stays empty after restart.

Instructions, translations and movement settings are stored offline in
`movement-reminders.db` inside the current OS user's AthanTime app-data directory,
resolved through .NET special-folder APIs. This writable file is separate from the
bundled read-only city database. `Data/movement-breaks.json` seeds original default
ideas only when a movement database is first created; it does not overwrite edits.
An early development database without stored translation order is upgraded
automatically and transactionally, preserving its settings and custom ideas.

In Settings > General, select Movement breaks and use Export or Import settings
to transfer the enabled state,
timing and every idea/translation between supported Athan Time installations. The
file is a portable backup, not a copy of the SQLite database. It excludes runtime
deadlines/history, prayer settings, audio and logs; review custom instructions before
sharing. Import validates the file before asking whether to replace the current
movement settings and ideas. Cancelling or choosing an invalid file changes nothing.
A confirmed import closes an open movement reminder and starts a fresh full interval.

The whole-app backup in Settings > General includes movement reminders. Older
settings-only files and individual settings sections keep the saved movement ideas.
Normal uninstall keeps these preferences. Choosing explicit current-user
settings removal also removes the known movement database and SQLite sidecars, while
keeping recordings, unknown files and other users' data. No settings are synced
online.

## Design references

[Stretchly's official description](https://hovancik.net/stretchly/about/) inspired
the general pattern of timed break ideas and postponement. No Stretchly code,
wording, artwork or Electron runtime is included.

[HSE's display-screen guidance](https://www.hse.gov.uk/msd/dse/work-routine.htm)
supports short, frequent breaks or changes of activity that allow movement and
posture changes; timing depends on the work. The
[NHS sitting-exercise guide](https://www.nhs.uk/live-well/exercise/sitting-exercises/)
describes gentle movements within a comfortable range. The bundled prompts are
original, brief suggestions, not copied exercise routines or medical claims.

## Verification

On 2026-11-20, Windows x64 Debug and Release each passed 572 of 579 tests
with seven symbolic-link permission skips. The suite covered concurrent
opening of an early development database, preservation of custom text and
timing, and missing sidecars without first-chance file-not-found exceptions.
Self-contained payload checks passed for all five targets.

On 2026-11-19, Windows x64 Debug and Release each passed **569 of 576
tests**, with seven symbolic-link permission skips in each configuration. Both
builds completed with zero errors using .NET SDK 11.0.100. Each reported two
NU1900 warnings because the NuGet vulnerability service was unreachable; this
was not a completed dependency-vulnerability audit.

Headless checks cover all eight languages and both themes. English light/Arabic
dark settings and English dark/Arabic light popup captures were visually reviewed.
Regressions cover import-language cache refresh and refusing edits after an initial
database-load failure until a successful reload; save-failure edits remain retryable.
An isolated same-thread probe measured zero allocated bytes for 100,000 quiet
movement-clock ticks after 10,000 warm-up ticks, matching the existing text clock.
It used no forced garbage collection and does not establish whole-process RAM,
CPU or GPU usage.

Fresh self-contained cross-publishes passed for `win-x64`, `linux-x64`,
`linux-arm64`, `osx-x64` and `osx-arm64`. Each payload matched the source seed
and package guide, excluded the writable user database and passed seven static
native-file architecture checks. Cross-publishing does not run the application;
native Windows/macOS/Linux desktop and installer checks were not run. Existing
published assets remain unchanged; previous beta.15 results are historical.

- Database: new-file seeding in eight languages, legacy translation-order migration,
  restart/edit/delete/empty-list persistence, optional-sidecar probing without
  debugger exceptions, transactional failure, bounds, invalid/future schemas,
  linked paths and exact consent-based cleanup; preserve translation fallback/order
  and retain other files and the location catalog.
- Transfer: atomic export, bounded/schema-valid round trip of preferences and all
  translations, stable order, refused database/sidecar destinations, malformed/
  future/duplicate/unsupported input, explicit confirmation and cancel/no-change.
  Successful import replaces only movement data and resets the popup/interval.
- Scheduling: no early reminder, exact due/expiry boundary, full pause after Done,
  five-minute Snooze, disabled/global pause, backward and greater-than-five-second
  gaps, sleep/resume, import/disposal and no queued catch-up. Test must expire while
  disabled/paused without advancing automatic rotation.
- Priority: silent prayer windows as well as audio block or replace movement;
  text Adhkar also takes priority. No overlapping movement window survives a reset.
- UI: all eight languages, both themes, Arabic/Urdu RTL, wrapped instructions,
  numeric LTR controls, editing-language fallback and visible storage/transfer
  failure help. Use the shared appearance and keep the popup nonactivating.
- Resources/packages: no SQL calls or additional timer on ticks; bounded cached
  text, closed connections, all-five-payload seed identity and existing SQLite
  native assets. Allocation checks do not establish whole-process RAM/CPU limits.
- Native Windows/macOS/Linux checks: popup positioning/focus, tray operation,
  suspend/resume and explicit cleanup require their matching desktop hosts.
