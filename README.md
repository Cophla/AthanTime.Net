# Athan Time

An offline desktop application for prayer times, Athan and Adhkar on Windows,
macOS and Linux.

[Project website](https://github.com/Cophla/AthanTime.Net) · [Releases and downloads](https://github.com/Cophla/AthanTime.Net/releases)

**Version: 1.0.2-beta.15 — Beta**

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

Every portable download includes [README.txt](packaging/README.txt).
Mac/Linux packages also include START-HERE.txt. On Mac, open Athan Time.app;
on Linux, use the included launcher. See the
[release description](docs/releases/v1.0.2-beta.15.md#release-description).

On first launch, confirm or change the suggested city before reminders start.
When updating, use Windows Setup or quit the app before replacing Mac/Linux files.
Uninstall keeps settings by default; optional settings removal is explained in the
included README. Windows uninstall confirms when removal succeeds.
Calculated prayer times may differ from your local mosque's timetable.

## Beta status

This is a beta release. Windows and Mac may show security warnings because the
downloads are unsigned. Wider desktop testing is still in progress. See
[compatibility and testing details](docs/PACKAGING.md#compatibility-and-validation-limits).

## Support

**athantimedotnet@gmail.com**

Support actions open a draft in your email program. Nothing is sent automatically.
Review exported logs before attaching or sharing them.

## Project information

- [Developer and AI-agent guide](AGENTS.md)
- [Installation and build details](docs/PACKAGING.md)
- [Product requirements](docs/REQUIREMENTS.md)
- [Third-party notices and credits](THIRD-PARTY-NOTICES.md)
