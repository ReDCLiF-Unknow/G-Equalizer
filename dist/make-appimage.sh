#!/bin/bash
# Run this on Linux x64 to produce a .AppImage from the tar.gz.
# Requirements: appimagetool (https://github.com/AppImage/AppImageKit/releases)
#   wget -O appimagetool "https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage"
#   chmod +x appimagetool
set -e

VERSION="3.0.0"
DIST="$(cd "$(dirname "$0")" && pwd)"
APPDIR="$DIST/GEqualizer.AppDir"

echo "==> Extracting tar.gz..."
rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin" "$APPDIR/usr/share/icons/hicolor/256x256/apps"

tar -xzf "$DIST/GEqualizer-linux-x64-$VERSION.tar.gz" -C "$DIST"
cp -r "$DIST/GEqualizer-linux/." "$APPDIR/usr/bin/"
chmod +x "$APPDIR/usr/bin/GamingEqualizer"

# AppImage desktop entry
cat > "$APPDIR/GEqualizer.desktop" <<'EOF'
[Desktop Entry]
Name=G-EQ
Exec=GamingEqualizer
Icon=GEqualizer
Type=Application
Categories=Audio;
EOF

# Copy icon (PNG extracted from .icns or use any 256×256 PNG)
# If you have a PNG, place it at: $APPDIR/GEqualizer.png
# Otherwise appimagetool will warn but still build.

# AppRun launcher
cat > "$APPDIR/AppRun" <<'APPRUN'
#!/bin/bash
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/bin/GamingEqualizer" "$@"
APPRUN
chmod +x "$APPDIR/AppRun"

echo "==> Building AppImage..."
ARCH=x86_64 ./appimagetool "$APPDIR" "$DIST/GEqualizer-linux-x64-$VERSION.AppImage"

rm -rf "$APPDIR" "$DIST/GEqualizer-linux"
echo "Done -> GEqualizer-linux-x64-$VERSION.AppImage"
