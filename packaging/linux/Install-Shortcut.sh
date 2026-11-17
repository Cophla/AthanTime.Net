#!/bin/bash
set -euo pipefail
if [[ "$(uname -s)" != Linux ]]; then
  echo 'Use this shortcut installer on Linux.' >&2
  exit 1
fi
HERE="$(cd -- "$(dirname -- "$0")" && pwd)"
chmod u+x "$HERE/Launch-Athan-Time.sh" "$HERE/AthanTime.Net"
DATA="${XDG_DATA_HOME:-$HOME/.local/share}"
if [[ "$DATA" != /* ]]; then DATA="$HOME/.local/share"; fi
APPLICATIONS="$DATA/applications"
ICONS="$DATA/icons/hicolor/256x256/apps"
mkdir -p "$APPLICATIONS" "$ICONS"
cp -- "$HERE/athan-time.png" "$ICONS/athan-time.png"
# Apply Exec quoting, then the desktop entry string escape layer. Percent is a field-code prefix.
COMMAND="$HERE/Launch-Athan-Time.sh"
COMMAND="${COMMAND//\\/\\\\}"
COMMAND="${COMMAND//\"/\\\"}"
COMMAND="${COMMAND//\$/\\\$}"
COMMAND="${COMMAND//\`/\\\`}"
COMMAND="${COMMAND//\\/\\\\}"
COMMAND="${COMMAND//%/%%}"
# Match Avalonia's default X11/XWayland WM_CLASS (the application assembly name).
printf '[Desktop Entry]\nType=Application\nName=Athan Time\nExec="%s"\nIcon=athan-time\nStartupWMClass=AthanTime.Net\nTerminal=false\nCategories=Utility;\n' "$COMMAND" > "$APPLICATIONS/athan-time.desktop"
echo 'Athan Time was added to your application menu. Keep the extracted folder in this location.'
