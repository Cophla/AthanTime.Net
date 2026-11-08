# Athan Time

An offline desktop application for prayer times, Athan and Adhkar on Windows,
macOS and Linux.

**Current version: 1.0.2-beta.7 — Beta**

## Technology

- **C# and .NET 10** — application development.
- **Avalonia** — desktop interface for Windows, macOS and Linux.
- **SQLite** — offline city search.
- Built with free, open-source technologies.

## Requirements

Choose the download that matches your computer:

| Operating system | Available versions |
| --- | --- |
| Windows | x64 (Intel/AMD 64-bit) |
| macOS | Intel and Apple Silicon |
| Linux | x64 and ARM64 |

- A compatible desktop operating system. See [compatibility details](docs/PACKAGING.md#compatibility-and-validation-limits).
- No separate .NET installation is needed for the release downloads.
- Speakers or headphones to hear Athan and Adhkar.
- Keep the application running and the computer awake for reminders.
- Building from source requires the .NET 10 SDK.

## Benefits and advantages

- **Works offline:** prayer times, city search and reminders need no internet connection.
- **No account required:** no registration, API keys or online service is needed.
- **Local settings:** your preferences stay on your computer.
- **Flexible reminders:** enable or disable notifications before, at and after each prayer, with adjustable before/after times.
- **Prayer times and sunrise:** shows the five prayers, with Shoroq/sunrise as a separate time marker.
- **Audio and Adhkar:** bundled recordings, custom Ogg Vorbis audio and text Adhkar.
- **Eight languages:** English, Arabic, French, Spanish, Turkish, German, Indonesian and Urdu.
- **Comfortable interface:** light and dark themes, plus right-to-left support for Arabic and Urdu.
- **Convenient daily use:** system-tray access where available, optional startup at login and settings backup/import/export.

## Getting started

1. Download the matching file from this repository's GitHub Releases page.
2. On Windows, run the setup file. For portable downloads, extract all files together.
3. Open the app, choose your city and review the calculation and reminder settings.

Linux/macOS portable ZIPs require a terminal launch; see the
[download instructions](docs/releases/v1.0.2-beta.7.md#release-description).
Calculated prayer times may differ from your local mosque's timetable.

## Beta status

This is a beta release. Windows downloads are unsigned and may be blocked by
Windows security features. Native desktop testing is still pending; Linux/macOS
ZIPs were built on Windows. See the [release notes](docs/releases/v1.0.2-beta.7.md)
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
