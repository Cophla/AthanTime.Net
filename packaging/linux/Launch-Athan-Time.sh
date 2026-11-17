#!/bin/sh
set -eu
if [ "$(uname -s)" != Linux ]; then
  echo 'Use this launcher on Linux.' >&2
  exit 1
fi
HERE="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
cd "$HERE"
chmod u+x ./AthanTime.Net
STATE="${XDG_STATE_HOME:-$HOME/.local/state}/AthanTime"
mkdir -p "$STATE"
# Retain native loader errors for troubleshooting, and detach all terminal streams.
nohup ./AthanTime.Net </dev/null >>"$STATE/launcher.log" 2>&1 &
printf 'Athan Time started. Startup diagnostics: %s/launcher.log\n' "$STATE"
