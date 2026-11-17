#!/usr/bin/env bash
# Shared Linux/macOS launcher; keep packaging decisions in InstallerBuilder.
set -u
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_GENERATE_ASPNET_CERTIFICATE=false

script_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)" || exit 1
cd -- "$script_directory" || exit 1

result=1
if command -v dotnet >/dev/null 2>&1; then
  echo "Creating the native Release installer for this computer..."
  dotnet run --project "$script_directory/tools/InstallerBuilder/InstallerBuilder.csproj" --configuration Release -- "$@"
  result=$?
else
  echo "The .NET 10 SDK is required. Install it and reopen this launcher." >&2
fi

if [[ "$result" -eq 0 ]]; then
  echo "Packaging command completed. Installer output: $script_directory/artifacts/packages"
else
  echo "Installer creation failed with exit code $result. See the error above." >&2
fi

# Keep file-manager launches visible, without blocking command-line automation.
if [[ "$#" -eq 0 && -t 0 ]]; then
  read -r -p "Press Enter to close..." _ || true
fi
exit "$result"
