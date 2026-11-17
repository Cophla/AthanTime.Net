# Athan Time

An offline desktop application for prayer times, Athan and Adhkar on Windows,
macOS and Linux.

[Project website](https://github.com/Cophla/AthanTime.Net) · [Releases and downloads](https://github.com/Cophla/AthanTime.Net/releases)

**Source version: 1.0.2-beta.15 — Beta release preparation**

## Technology

- **C# and .NET 11** — application development.
- **Avalonia** — desktop interface for Windows, macOS and Linux.
- **SQLite** — offline city search.
- Built with free, open-source technologies.

## Requirements

Choose the download that matches your computer:

| Operating system | Download name contains |
| --- | --- |
| Windows | `Windows-x64-Setup` or `Windows-x64-Portable` |
| macOS 15 or newer | `macOS-Intel-x64` or `macOS-AppleSilicon-arm64` |
| Linux | `Linux-x64` or `Linux-arm64` |

- A compatible desktop operating system. See [compatibility details](docs/PACKAGING.md#compatibility-and-validation-limits).
- All application downloads are self-contained: no separate .NET installation is needed.
- Speakers or headphones to hear Athan and Adhkar.
- Keep the application running and the computer awake for reminders.
- Building from source requires the .NET 11 SDK.

## Benefits and advantages

- **Works offline:** prayer times, city search and reminders need no internet connection.
- **No account required:** no registration, API keys or online service is needed.
- **Local settings:** your preferences stay on your computer.
- **Flexible reminders:** enable or disable notifications before, at and after each prayer, with adjustable before/after times.
- **Prayer times and sunrise:** shows the five prayers, with Shoroq/sunrise as a separate time marker.
- **Audio and Adhkar:** bundled recordings, custom Ogg Vorbis audio and text Adhkar, with Test buttons to try reminders immediately.
- **Eight languages:** English, Arabic, French, Spanish, Turkish, German, Indonesian and Urdu.
- **Comfortable interface:** light and dark themes, plus right-to-left support for Arabic and Urdu.
- **Fits your screen:** adjusts the window for the monitor's available space and display scaling.
- **Convenient daily use:** system-tray access where available, optional startup at login and settings backup/import/export.

## Getting started

1. Download the matching file from [Releases and downloads](https://github.com/Cophla/AthanTime.Net/releases).
2. On Windows, run the setup file. For portable downloads, extract all files together.
3. Open the app, choose your city and review the calculation and reminder settings.

For local builds, the latest verified downloads and package-selection README are
in `artifacts/packages`. Every portable download includes
[README.txt](packaging/README.txt); Mac/Linux packages also include START-HERE.txt.

The beta.15 preparation includes a macOS app bundle and Linux desktop launchers; see [desktop launch instructions](docs/PACKAGING.md#desktop-launchers-2026-11-15). See the
[beta.15 download instructions](docs/releases/v1.0.2-beta.15.md#release-description).
Calculated prayer times may differ from your local mosque's timetable.

## Beta status

This is a beta release. Windows downloads are unsigned and may be blocked by
Windows security features. Full installation/audio/tray testing remains pending; Linux/macOS
ZIPs were built on Windows. See the [release preparation notes](docs/releases/v1.0.2-beta.15.md)
for current limitations.

## Support

**athantimedotnet@gmail.com**

Support actions open a draft in your email program. Nothing is sent automatically.
Review exported logs before attaching or sharing them.

## Project information

- [Developer and AI-agent guide](AGENTS.md)
- [Installation and build details](docs/PACKAGING.md)
- [Product requirements](docs/REQUIREMENTS.md)
- [Third-party notices and credits](THIRD-PARTY-NOTICES.md)

Windows setup requests graceful app exit before an update and opens it after successful interactive installation. Older versions may need manual Exit once. Silent setup does not open the app. On macOS/Linux, quit before replacing files, then open the app from Finder or its desktop shortcut.

Windows uninstall shows a confirmation when removal completes successfully.
Silent uninstall stays unattended. On macOS/Linux, use the normal Finder or package-manager removal workflow.

On first launch, the app uses your OS language and 12/24-hour preference and suggests a city from your region/time zone. Confirm or change the city before prayer reminders start. Existing settings stay unchanged.

Uninstall keeps settings by default. Windows offers an optional Remove my settings checkbox. On Mac/Linux, run the included Remove-My-Settings helper as your normal user before removing the app. Other users and custom recordings are kept.

Windows setup adds an Uninstall choice for an existing installation and requests graceful app exit directly, including from the tray. Start uninstall normally, even for All users: optional settings cleanup runs as your user before the administrator prompt for shared-file removal. Run as administrator keeps settings and disables that option. Cleanup errors offer Retry, keep remaining settings and continue, or Abort. The current local packages are in `artifacts/packages`; obsolete generated downloads were removed. Windows preferences are stored separately for each user in `%APPDATA%\AthanTime`.

On Linux, run `bash Install-Shortcut.sh` from the extracted folder to add Athan Time
to your menu with its correct dock icon. After updating, rerun it, fully exit/reopen
the app, and replace any older generic shortcut pinned to the dock.
