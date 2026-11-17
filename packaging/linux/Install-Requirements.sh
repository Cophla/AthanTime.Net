#!/bin/bash
set -euo pipefail
if [[ "$(uname -s)" != Linux ]] || ! command -v apt-get >/dev/null; then
  echo 'This helper supports Ubuntu/Debian desktop Linux with apt.' >&2
  exit 1
fi
SUDO=()
if [[ "$EUID" -ne 0 ]]; then SUDO=(sudo); fi
"${SUDO[@]}" apt-get update
PACKAGES=(ca-certificates libbrotli1 libc6 libgcc-s1 libgssapi-krb5-2 libstdc++6
  zlib1g libx11-6 libice6 libsm6 libfontconfig1 libgl1 libpulse0)
# Distribution releases rename these libraries; select an available package in each group.
for GROUP in 'libicu78 libicu76 libicu74 libicu72 libicu70' 'libssl3t64 libssl3' 'libasound2t64 libasound2'; do
  FOUND=false
  for PACKAGE in $GROUP; do
    if apt-cache show "$PACKAGE" >/dev/null 2>&1; then
      PACKAGES+=("$PACKAGE")
      FOUND=true
      break
    fi
  done
  if [[ "$FOUND" != true ]]; then
    echo "Cannot find required library package: $GROUP" >&2
    exit 1
  fi
done
"${SUDO[@]}" apt-get install "${PACKAGES[@]}"
