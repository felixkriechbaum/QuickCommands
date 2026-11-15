#!/usr/bin/env bash
# Builds QuickCommands.app from a `dotnet publish -r osx-*` folder and zips it.
# Usage: make-app.sh <publishDir> <version> <arch> <outDir>      (runs on macOS: needs sips, iconutil, codesign, ditto)
set -euo pipefail

publish="$1"
version="$2"
arch="$3"
out="$4"
here="$(cd "$(dirname "$0")" && pwd)"

app="$out/QuickCommands.app"
rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"

cp "$publish/QuickCommands" "$app/Contents/MacOS/QuickCommands"
chmod +x "$app/Contents/MacOS/QuickCommands"
sed "s/__VERSION__/$version/g" "$here/Info.plist" > "$app/Contents/Info.plist"

iconset="$(mktemp -d)/QuickCommands.iconset"
mkdir -p "$iconset"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$here/../icon-1024.png" --out "$iconset/icon_${size}x${size}.png" >/dev/null
  double=$((size * 2))
  sips -z "$double" "$double" "$here/../icon-1024.png" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$app/Contents/Resources/QuickCommands.icns"

# Apple Silicon refuses to run unsigned code – an ad-hoc signature is enough (the app is not notarized).
codesign --force --deep --sign - "$app"

ditto -c -k --keepParent "$app" "$out/QuickCommands-$version-macos-$arch.zip"
echo "$out/QuickCommands-$version-macos-$arch.zip"
