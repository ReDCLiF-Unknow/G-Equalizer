#!/bin/bash
# Run this on macOS to produce .dmg files from the zipped .app bundles.
# Requirements: macOS 12+, Xcode command-line tools (for hdiutil).
set -e

VERSION="3.0.0"
DIST="$(cd "$(dirname "$0")" && pwd)"

make_dmg() {
    local ZIP="$1"        # e.g. GEqualizer-macOS-arm64-3.0.0.zip
    local ARCH="$2"       # e.g. arm64
    local STAGING="$DIST/.dmg-staging-$ARCH"

    echo "==> Building macOS-$ARCH .dmg..."
    rm -rf "$STAGING"
    mkdir -p "$STAGING"

    # Unzip the .app bundle
    unzip -q "$DIST/$ZIP" -d "$STAGING"

    # Fix executable bit (lost in zip on Windows)
    chmod +x "$STAGING/GEqualizer-$ARCH.app/Contents/MacOS/GamingEqualizer"

    # Create the .dmg
    hdiutil create \
        -volname "G-EQ $VERSION" \
        -srcfolder "$STAGING" \
        -ov -format UDZO \
        "$DIST/GEqualizer-macOS-$ARCH-$VERSION.dmg"

    rm -rf "$STAGING"
    echo "    -> GEqualizer-macOS-$ARCH-$VERSION.dmg"
}

make_dmg "GEqualizer-macOS-arm64-$VERSION.zip" "arm64"
make_dmg "GEqualizer-macOS-x64-$VERSION.zip"   "x64"

echo "Done."
