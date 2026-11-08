#!/usr/bin/env bash
# Shared by the direct Bash recipes; the repository props remain the version source.
athan_recipe_version() {
  local repository="$1" version="${2:-}"
  if [[ -z "$version" ]]; then
    version="$(sed -n 's|.*<Version>\([^<]*\)</Version>.*|\1|p' "$repository/Directory.Build.props")"
  fi
  if [[ ! "$version" =~ ^[0-9]+(\.[0-9]+){1,3}(-[A-Za-z0-9.-]+)?$ ]]; then
    echo "Version must be a numeric dotted version, optionally with a prerelease suffix." >&2
    return 2
  fi
  printf '%s\n' "$version"
}

athan_require_host() {
  if [[ "$(uname -s)" != "$1" ]]; then
    echo "This native package must be built on $1. Use InstallerBuilder portable for a cross-OS ZIP." >&2
    return 2
  fi
}

athan_host_architecture() {
  case "$(uname -m)" in
    x86_64|amd64) printf '%s\n' 'x64' ;;
    aarch64|arm64) printf '%s\n' 'arm64' ;;
    *) echo "Supported native CPU architectures are x64 and arm64." >&2; return 2 ;;
  esac
}
