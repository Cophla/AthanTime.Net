Athan Time
Offline prayer times, Athan reminders and Adhkar

GETTING STARTED

These packages include the .NET runtime. You do not need to install .NET separately.
Extract portable ZIPs completely and keep their files together. The app works
offline, without an account. Reminders need the app running and the computer awake.

Windows x64
Run the Setup file for installation, or extract the Portable ZIP and open
AthanTime.Net.exe. Setup offers Only me and All users. Existing installations can
be updated or uninstalled through setup. All users requires administrator permission.

macOS 15 or later
Choose Intel x64 or Apple Silicon ARM64 to match your Mac. Extract the complete ZIP
and open Athan Time.app, or move the app into Applications. The optional
Launch-Athan-Time.command opens the app without keeping Terminal open.
See START-HERE.txt for details.

Linux desktop
Choose x64 for Intel/AMD or ARM64 for ARM devices. From the extracted folder, run:
  sh Launch-Athan-Time.sh
For a menu shortcut and the correct Ubuntu dock icon, run once:
  bash Install-Shortcut.sh
Then fully exit/reopen Athan Time from the application menu. Replace any older
generic shortcut pinned to the dock. Rerun the helper after moving/updating the folder.
You can close Terminal after launch. See START-HERE.txt for OS-library requirements.

FIRST LAUNCH

Review the suggested city, calculation method and reminder settings. Confirm or
change the city before automatic prayer reminders begin. Existing settings stay
unchanged. Sunrise/Shoroq is a time marker, not a prayer.

UPDATING AND REMOVING

On Windows, use the latest setup to update. Updated apps receive a graceful exit
request; an older app may need Exit from its tray menu once. On macOS/Linux, fully
quit before replacing the application files. Keep the app's location unchanged
when possible; after moving it, disable/re-enable Start on login.

Settings are individual to each OS user and are kept by default. Windows uninstall
offers an optional Remove my settings checkbox. Start it normally, even for an
All users installation. Selected settings are removed as your current user before
the administrator prompt for shared-file removal; cancelling that later prompt
does not restore settings. A Run as administrator launch keeps settings and
disables that checkbox. Portable packages include a
Remove-My-Settings helper; run it before removing the app only if you want to reset
your preferences. On macOS/Linux use your normal user, without sudo. Other users
and custom recordings are preserved. See the helper's confirmation before proceeding.

MOVEMENT BREAKS

Open Settings > Movement breaks to enable optional reminders to step away from
the screen. Choose how long to wait after each reminder and how long it stays
visible. Done starts the next wait; Snooze postpones it by five minutes.
Test tries an idea without enabling automatic reminders.

The supplied ideas support all eight app languages. You can add your own ideas
and enter translations yourself, offline. Move only as comfortably as you can;
stop if you feel pain or dizziness. These are gentle prompts, not medical advice.

Movement settings and ideas are stored in your own local movement-reminders.db.
Open Settings > General to export or import a backup. Choose All settings or a
section. Choose Movement breaks to back up just its timing, ideas and
translations. Import asks for confirmation. Review custom text before sharing.
General also lets you adjust the reading highlight speed for both text popups.

To start over, use Restore factory settings in Settings > General. After
confirmation, Athan Time clears your local preferences and closes. Open it again
for first-run setup. Your recordings and exported backups are kept.
Explicit settings removal deletes the movement database too; normal uninstall keeps it.

BETA AND SUPPORT

This is beta software. Check About for the version. Windows packages are unsigned;
macOS packages are unsigned and not notarized. Follow your OS and organization
security policies. Native installation and desktop testing remain in progress.

Project: https://github.com/Cophla/AthanTime.Net
Downloads: https://github.com/Cophla/AthanTime.Net/releases
Support: athantimedotnet@gmail.com

See the included licenses folder for third-party notices. Supplied recordings'
upstream redistribution rights remain unverified; review the notices before sharing.
