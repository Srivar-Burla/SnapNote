#!/bin/bash
# ---------------------------------------------------------------
#  SnapNote - one-time setup for macOS
#  Builds SnapNote.app in this folder from main.swift, using Apple's
#  own Swift compiler (Xcode Command Line Tools). Nothing else is
#  downloaded or installed.
#  NOTE: written on Windows and NOT yet tested on a Mac. If a step
#  fails, see "If something goes wrong" in README.md.
# ---------------------------------------------------------------
cd "$(dirname "$0")" || exit 1
DIR="$(pwd)"

fail() { echo; echo "[!] $1"; echo "    See README.md."; read -n 1 -s -r -p "Press any key to close."; exit 1; }

echo
echo "=== SnapNote setup ==="
echo

# Apple's Swift compiler comes with the Command Line Tools.
if ! xcrun --find swiftc >/dev/null 2>&1; then
  echo "Apple's Command Line Tools are needed to build SnapNote (a one-time, free install from Apple)."
  xcode-select --install 2>/dev/null
  fail "Finish the Command Line Tools install in the window that opened, then run this setup again."
fi
xcrun swiftc --version 2>&1 | head -1

# Quit a running copy so it can be replaced
osascript -e 'tell application "SnapNote" to quit' >/dev/null 2>&1
pkill -x SnapNote >/dev/null 2>&1

ARCH="$(uname -m)"
APP="$DIR/SnapNote.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources" || fail "Could not create SnapNote.app here."

echo "Building SnapNote.app for $ARCH (about 30 seconds) ..."
xcrun swiftc -O -swift-version 5 -target "$ARCH-apple-macos11.0" \
  -o "$APP/Contents/MacOS/SnapNote" main.swift \
  -framework AppKit -framework Carbon -framework CoreImage -framework ApplicationServices \
  || fail "The build failed. Send the messages above to Claude."

cp AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"
cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>SnapNote</string>
  <key>CFBundleDisplayName</key><string>SnapNote</string>
  <key>CFBundleIdentifier</key><string>local.linework.snapnote</string>
  <key>CFBundleVersion</key><string>0.2</string>
  <key>CFBundleShortVersionString</key><string>0.2</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleExecutable</key><string>SnapNote</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>LSUIElement</key><true/>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST

# Sign it for this Mac only, so macOS remembers the Screen Recording and Accessibility permissions.
codesign --force --sign - --identifier local.linework.snapnote "$APP" >/dev/null 2>&1 || echo "(Signing skipped; the app still runs.)"
# Files from a downloaded zip are quarantined; clear that for this folder only.
xattr -dr com.apple.quarantine "$DIR" 2>/dev/null
touch "$APP"

echo
echo "Setup finished. Opening SnapNote ..."
open "$APP"
echo
echo "SnapNote lives in the menu bar (crop-marks icon). Press Control+Shift+S to capture."
echo "The first time, macOS asks to allow Screen Recording: allow it, then quit SnapNote"
echo "from its menu bar icon and open SnapNote.app again. README.md has the full checklist."
echo
read -n 1 -s -r -p "Press any key to close."
echo
