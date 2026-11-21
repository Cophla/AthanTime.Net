# Local audio and text Adhkar

## Prayer reminders

Open **Settings → Audio & Adhkar → Prayer reminders → Notification settings**
and expand the prayer. Three separate sections control its reminders:

- **Notify before prayer**: choose 1–180 minutes before Athan; the default is
  **15 minutes**, and **30 minutes** is supported.
- **Notify at prayer time**: notify exactly at the displayed prayer/Athan time.
- **Notify after prayer**: choose 1–180 minutes after Athan; the default is
  **5 minutes**, with this reminder initially disabled.

Each section has its own enable switch for each prayer. Turning one off does
not turn the others off, and its minute value is retained for later use.
The master prayer reminders switch disables all prayer reminders together.
The five prayers have before/at-time reminders enabled by default; sunrise is
opt-in. Existing profiles keep their saved choices, including a previously
saved ten-minute setting.

**Shoroq/sunrise is a time marker, not a prayer.** Its row uses **Notify before
sunrise**, **Notify at sunrise**, and **Notify after sunrise**, with sunrise-specific
popup titles/messages in all eight languages. It keeps the same displayed time,
independent minute values, optional audio and saved choices. It is excluded from
current/next-prayer countdowns. The master switch and pause still apply to it.

The advance reminder shows a popup without playing Athan early. **Notify at prayer
time** and the audio controls independently govern the prayer-time popup and Athan.
Keep the application running with the master prayer reminders switch enabled and
reminders unpaused. Missed reminders during sleep or exit are not replayed.

## Audio

Use the **Athan audio** panel to select one local Ogg Vorbis (.ogg) per prayer or sunrise.
Enable the master reminders switch, that event's at-time reminder, and its audio
checkbox. An empty custom path uses the bundled Nasser Alqatami Athan Ogg; **Clear** restores it.
The defaults use the supplied NasserAlqatamiAthan.ogg and
NasserAlqatamiAllahoAkbar.ogg recordings. Existing custom selections continue
to use their saved files. Recording provenance is listed in
[third-party notices](../THIRD-PARTY-NOTICES.md). Before/after reminders remain
silent. **Play** previews a file;
**Stop audio** is also available in the tray menu and prayer popup.
Volume changes apply to the next playback. The tray can also play the bundled
Athan or Takbeer immediately, or enable/disable each automatic audio category.

Under **Adhkar recordings**, the bundled Nasser Alqatami Takbeer clip
is included by default; add multiple custom Ogg Vorbis files if desired. One file plays each interval.
Sequential mode repeats the list; random mode chooses one entry per interval
(repeats are possible). Move up changes the sequential order. Busy intervals
are skipped, and prayer audio interrupts Adhkar. Editing the list or its interval
restarts the wait. Language/theme changes do not restart it.

Files are referenced, not copied. Moving/deleting a recording requires selecting
it again. Missing files or audio-device failures are logged and shown in the UI.
Only local, fully qualified .ogg paths are accepted; UNC/network URLs are rejected.
Ogg Opus and MP3 are not supported. Old custom paths remain in settings but must be
converted to Vorbis and reselected; the app never changes or deletes user recordings.
As with any desktop program, an OS-mounted remote folder can appear as a local path.

## Package choice and platform requirements

[SoundFlow 1.4.2](https://www.nuget.org/packages/SoundFlow/1.4.2) is MIT licensed.
Its NuGet package includes [miniaudio](https://miniaud.io/) native binaries for output.
[NVorbis 0.10.5](https://www.nuget.org/packages/NVorbis/0.10.5) is an MIT-licensed
managed Vorbis decoder. `AudioPlayer.cs` contains the output adapter, streaming
`VorbisAudioProvider` and installation-local `BundledAudio` paths. The provider
never buffers a full recording; its SoundFlow positions count interleaved samples.
Seeking exactly to the end represents EOF without asking NVorbis to seek beyond
its last decodable page; a later seek can restart reading. Unit tests cover empty
reads, invalid offsets, EOF notification/restart and repeated disposal without
opening an audio device.
The [deployment policy](PACKAGING.md#deployment-policy) retains this shared C#
adapter with per-OS/CPU native output libraries. Native backend files are part of
its cross-platform support and ship in the self-contained packages.
SoundFlow's core decoder does not support Vorbis. No VLC, FFmpeg, external player,
or manual native build is required. This is **NuGet-only deployment, not pure managed
audio**: device access still uses packaged native code and OS sound services.

Publish for the target RID, including its native libraries, without trimming or
single-file bundling. Target desktop RIDs are win-x64, linux-x64, linux-arm64,
osx-x64 and osx-arm64. A functioning OS sound device/service remains necessary;
headless servers and Linux containers need separate audio configuration.

SoundFlow's maintainer announced limited support through February 2027. The version
is pinned and the app uses only basic file playback behind `IAudioPlayer`, making
replacement possible without changing prayer or playlist logic. Review upstream
maintenance/security before upgrading. Do not describe it as equally mature as VLC.

The engine/device are created only for playback, streams are decoded incrementally,
and all playback resources are released on completion or Stop. Native operations
run off the UI thread; callbacks queue cleanup instead of disposing their own device.

## Test buttons

Each Audio & Adhkar tab has a **Test** button:

- **Prayer reminders** shows the next actual prayer's popup and plays its selected
  audio when the master audio switch and that prayer's audio switch are enabled.
- **Adhkar recordings** plays the selected recording, otherwise the first playlist
  entry, otherwise bundled Takbeer. Stop audio ends playback.
- **Text Adhkar** displays the selected text, otherwise the first saved text,
  otherwise a current bundled reading. It closes after the configured display
  duration, including when automatic readings are disabled or reminders are paused.
  Dismissal starts a full reading pause without advancing the automatic rotation.

Tests run immediately without enabling automatic reminders, writing settings or
recording prayer-reminder history. A prayer test dismisses an existing reading
and uses the same themed popup as automatic reminders. File/device failures use
the existing audio status messages. The shared app clock manages text-test expiry.

## Text Adhkar

The **Text Adhkar** tab supports adding, editing and removing complete texts,
including multiline entries. With no custom entries, the bundled JSON supplies
Arabic morning, evening, and general remembrance with short meanings in all eight
UI languages. Morning entries appear during local hours 04:00–11:59, evening entries
during 16:00–23:59; this is an app scheduling convention, not a religious time ruling.
Adding a custom text replaces the bundled rotation until custom entries are cleared.
Each entry cites a Sahih hadith in `Data/default-adhkar.json`; the app does not count recitations.

Enable reading reminders and choose a display duration (5–600 seconds, initially
5) and a pause (1–1440 minutes). After the first pause:

1. One text appears in a small, scrollable, non-activating window.
2. It closes after the reading duration (or when dismissed early).
3. A full pause follows, then the next entry appears. The list wraps.

A colored character moves at eight characters per second by default. Change
the pace under Settings > General. For longer readings, increase the time the
popup stays open.

Disable or edit settings to close any current reading and restart the cycle.
Sleep/resume never queues missed windows. Prayer reminders dismiss the reading
and restart its pause. Text direction follows its first letter, independently of
the interface language. The window follows live UI language and theme changes.
Window placement/topmost behavior can be constrained by the desktop compositor.

The always-visible toolbar, settings page, and tray can pause all automatic prayer,
audio, and text reminders
for a chosen number of minutes. Current sound and text stop immediately; missed
intervals are never queued. The tray can independently start/stop prayer audio,
Adhkar audio, and text Adhkar. A JSON-backed diagnostic switch changes Serilog
from Information to Debug at runtime. Logs contain settings changes and failures,
not the content of custom Adhkar texts.

All settings use the existing per-OS-user JSON file. Audio/text schedules run only
while the application is running and the computer is awake; these are not OS alarms.

## Default recording verification (2026-11-15)

Replaced bundled Athan and Takbeer with the supplied Nasser Alqatami Ogg files.
Two asset-identity cases pin their SHA-256 hashes, and the complete streaming
decode test also checks decoder errors. Existing tests cover non-silent finite
samples, EOF/restart, seeks, disposal, default prayer playback, bundled playlist
identities and saved custom paths. No runtime playback abstraction was added.

On Windows x64, SDK 11.0.100, Debug and Release each built with zero warnings/errors
and passed all 463 tests. Build commands used -p:UsedAvaloniaProducts= to bypass
the build-telemetry task after its sandbox-denied local log write. These tests
decode without an audio device. Actual speaker playback and matching-OS
macOS/Linux audio remain unverified for the supplied recordings. All five
platforms consume the same Ogg assets through the existing streaming adapter.
Release creation is deferred until requested by the user.

The former bundled Alhamdulillah clip was removed at the user’s request on
November 15. Loaded/imported playlists drop only its retired bundled identifier,
case-insensitively; custom recording paths remain available.

After removing the former bundled Alhamdulillah entry and adding the desktop
launchers, the full Windows Debug and Release suites each passed 477 tests.
See the dated desktop-launcher evidence in [packaging](PACKAGING.md#desktop-launchers-2026-11-15).
