#!/usr/bin/env bash
set -euo pipefail

REPOSITORY="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
source "$REPOSITORY/packaging/common.sh"
VERSION="$(athan_recipe_version "$REPOSITORY" "${1:-}")"
NUMERIC_VERSION="${VERSION%%-*}"
if [[ ! "$NUMERIC_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Native macOS bundles require a three-part numeric base version (for example 1.2.3-beta.1)." >&2
  exit 2
fi
athan_require_host Darwin
RID="${2:-osx-$(athan_host_architecture)}"
if [[ "$RID" != "osx-arm64" && "$RID" != "osx-x64" ]]; then
  echo "RID must be osx-arm64 or osx-x64" >&2
  exit 2
fi

command -v hdiutil >/dev/null
WORK="$(mktemp -d)"
trap 'rm -rf -- "$WORK"' EXIT
PUBLISH="$WORK/publish"
STAGE="$WORK/image"
APP="$STAGE/Athan Time.app"
PLATFORM="macOS-Intel-x64"
if [[ "$RID" == "osx-arm64" ]]; then PLATFORM="macOS-AppleSilicon-arm64"; fi
OUTPUT="$REPOSITORY/artifacts/packages/AthanTime-$VERSION-$PLATFORM.dmg"

dotnet publish "$REPOSITORY/AthanTime.Net/AthanTime.Net.csproj" \
  -c Release -r "$RID" --self-contained true -p:Version="$VERSION" \
  -p:PublishSingleFile=false -p:DebugType=None -o "$PUBLISH"

mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources" "$REPOSITORY/artifacts/packages"
cp -a "$PUBLISH/." "$APP/Contents/MacOS/"
chmod 755 "$APP/Contents/MacOS/AthanTime.Net"
sed "s/@VERSION@/$VERSION/g; s/@NUMERIC_VERSION@/$NUMERIC_VERSION/g" "$REPOSITORY/packaging/macos/Info.plist.template" \
  > "$APP/Contents/Info.plist"

ln -s /Applications "$STAGE/Applications"
cp "$PUBLISH/README.txt" "$STAGE/README.txt"
cp "$REPOSITORY/packaging/macos/START-HERE.txt" "$STAGE/START-HERE.txt"
cp "$REPOSITORY/packaging/Remove-My-Settings.sh" "$STAGE/Remove-My-Settings.command"
chmod 755 "$STAGE/Remove-My-Settings.command"
hdiutil create -volname "Athan Time $VERSION" -srcfolder "$STAGE" -ov -format UDZO "$OUTPUT"
echo "Created $OUTPUT"
