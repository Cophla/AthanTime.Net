#!/usr/bin/env bash
set -euo pipefail

REPOSITORY="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
source "$REPOSITORY/packaging/common.sh"
VERSION="$(athan_recipe_version "$REPOSITORY" "${1:-}")"
# Debian's tilde sorts a prerelease before its final release; retain SemVer in UI/filenames.
DEBIAN_VERSION="$VERSION"
if [[ "$VERSION" == *-* ]]; then DEBIAN_VERSION="${VERSION%%-*}~${VERSION#*-}"; fi
athan_require_host Linux
ARCH="${2:-}"
if [[ -z "$ARCH" ]]; then
  ARCH="$(athan_host_architecture)"
  if [[ "$ARCH" == "x64" ]]; then ARCH="amd64"; fi
fi
RID="linux-x64"
if [[ "$ARCH" == "arm64" ]]; then RID="linux-arm64"; fi
if [[ "$ARCH" != "amd64" && "$ARCH" != "arm64" ]]; then
  echo "Architecture must be amd64 or arm64" >&2
  exit 2
fi
command -v dpkg-deb >/dev/null

OUTPUT="$REPOSITORY/artifacts/packages"
WORK="$(mktemp -d)"
PUBLISH="$WORK/publish"
STAGE="$WORK/package"
trap 'rm -rf -- "$WORK"' EXIT

dotnet publish "$REPOSITORY/AthanTime.Net/AthanTime.Net.csproj" \
  -c Release -r "$RID" --self-contained true -p:Version="$VERSION" \
  -p:PublishSingleFile=false -p:DebugType=None -o "$PUBLISH"

mkdir -p "$STAGE/DEBIAN" "$STAGE/opt/athan-time" "$STAGE/usr/bin" \
  "$STAGE/usr/share/applications" "$STAGE/usr/share/icons/hicolor/256x256/apps" "$OUTPUT"
cp -a "$PUBLISH/." "$STAGE/opt/athan-time/"
chmod 755 "$STAGE/opt/athan-time/AthanTime.Net"
ln -s /opt/athan-time/AthanTime.Net "$STAGE/usr/bin/athan-time"
cp "$REPOSITORY/AthanTime.Net/Assets/athan-icon.png" \
  "$STAGE/usr/share/icons/hicolor/256x256/apps/athan-time.png"

sed "s/@VERSION@/$DEBIAN_VERSION/g; s/@RELEASE_VERSION@/$VERSION/g; s/@ARCH@/$ARCH/g" \
  "$REPOSITORY/packaging/linux/control.template" > "$STAGE/DEBIAN/control"
sed "s/@VERSION@/$VERSION/g" "$REPOSITORY/packaging/linux/athan-time.desktop" \
  > "$STAGE/usr/share/applications/athan-time.desktop"
dpkg-deb --root-owner-group --build "$STAGE" "$OUTPUT/AthanTime-$VERSION-$ARCH.deb"
echo "Created $OUTPUT/AthanTime-$VERSION-$ARCH.deb"
