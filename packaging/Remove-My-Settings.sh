#!/usr/bin/env bash
set -euo pipefail

# User-run consent helper, never a root package-manager removal hook.
if [[ "${EUID:-$(id -u)}" == 0 ]]; then
  echo "Run this as your normal desktop user, without sudo. No settings were removed." >&2
  exit 1
fi
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
case "$(uname -s)" in
  Darwin)
    APP="$HERE/Athan Time.app/Contents/MacOS/AthanTime.Net"
    if [[ ! -x "$APP" ]]; then APP="/Applications/Athan Time.app/Contents/MacOS/AthanTime.Net"; fi
    ;;
  Linux)
    APP="$HERE/AthanTime.Net"
    if [[ ! -x "$APP" ]]; then APP="/opt/athan-time/AthanTime.Net"; fi
    ;;
  *) echo "Use this helper on macOS or Linux." >&2; exit 1 ;;
esac
if [[ ! -x "$APP" ]]; then echo "Keep the app installed until settings removal finishes." >&2; exit 1; fi
echo "Fully exit Athan Time first (including the tray)."
echo "Remove your settings, backups, window position, app logs and login startup?"
echo "Other users and custom recordings are kept. The application itself is kept."
read -r -p 'Type REMOVE to confirm: ' ANSWER || exit 0
if [[ "$ANSWER" != REMOVE ]]; then echo "Cancelled. Settings kept."; exit 0; fi
if "$APP" --remove-user-settings; then
  echo "Settings removed. You can now remove the app using Finder or your package manager."
else
  RESULT=$?
  echo "Settings cleanup failed with exit code $RESULT. Close all app copies and check access, then retry." >&2
  exit 1
fi
