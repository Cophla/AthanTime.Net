#!/usr/bin/env bash
# macOS opens executable .command files in Terminal.
script_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)" || exit 1
exec /bin/bash "$script_directory/Create-Installers.sh" "$@"
