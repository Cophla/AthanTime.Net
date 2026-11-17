#!/bin/bash
set -eu
if [ "$(uname -s)" != Darwin ]; then
  echo 'Use this launcher on macOS.' >&2
  exit 1
fi
HERE="$(cd -- "$(dirname -- "$0")" && pwd)"
chmod u+x "$HERE/Athan Time.app/Contents/MacOS/AthanTime.Net"
# Launch Services owns the app lifetime so closing Terminal does not stop it.
exec open "$HERE/Athan Time.app"
