#!/bin/bash
# Builds "Frequency Customizer.app" (Apple Silicon + Intel) and zips it for download.
# Run on a Mac (or the GitHub Actions macOS runner) from anywhere:  bash mac/scripts/make-app.sh
set -euo pipefail
cd "$(dirname "$0")/.."

VERSION="${VERSION:-1.0.0}"
APP="build/Frequency Customizer.app"

swift build -c release --arch arm64 --arch x86_64
BIN_DIR="$(swift build -c release --arch arm64 --arch x86_64 --show-bin-path)"

rm -rf build
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN_DIR/FrequencyCustomizer" "$APP/Contents/MacOS/FrequencyCustomizer"
sed "s/__VERSION__/$VERSION/g" Resources/Info.plist > "$APP/Contents/Info.plist"

swift scripts/make-icon.swift build/AppIcon.iconset
iconutil -c icns build/AppIcon.iconset -o "$APP/Contents/Resources/AppIcon.icns"

# Ad-hoc signature (no paid Apple account). Required for Apple Silicon to run the app at all.
codesign --force --deep --sign - "$APP"
codesign --verify --verbose "$APP"
lipo -info "$APP/Contents/MacOS/FrequencyCustomizer"

(cd build && ditto -c -k --sequesterRsrc --keepParent "Frequency Customizer.app" "FrequencyCustomizer-mac.zip")
echo "Built mac/build/FrequencyCustomizer-mac.zip"
