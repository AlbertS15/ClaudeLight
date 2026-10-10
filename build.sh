#!/bin/zsh
# Builds Lumi.app and installs it into ~/Applications.
set -euo pipefail
cd "${0:A:h}"

APP=build/Lumi.app
# The release workflow passes the tag's version; a local build takes the latest tag, if any.
VERSION=${LUMI_VERSION:-$(git describe --tags --abbrev=0 2>/dev/null | sed 's/^v//' || true)}
VERSION=${VERSION:-0.0.0}
rm -rf build
mkdir -p $APP/Contents/MacOS $APP/Contents/Resources

swiftc -O -swift-version 5 -target arm64-apple-macos14.0 \
  -framework AppKit -framework SwiftUI -framework Carbon -framework ServiceManagement \
  Sources/*.swift -o $APP/Contents/MacOS/Lumi

swift Icon/make_icon.swift build/AppIcon.iconset
iconutil -c icns build/AppIcon.iconset -o $APP/Contents/Resources/AppIcon.icns

cat > $APP/Contents/Info.plist <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Lumi</string>
  <key>CFBundleDisplayName</key><string>Lumi</string>
  <key>CFBundleIdentifier</key><string>io.github.alberts15.claudelight</string>
  <key>CFBundleExecutable</key><string>Lumi</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleVersion</key><string>1</string>
  <key>LSMinimumSystemVersion</key><string>14.0</string>
  <key>LSUIElement</key><true/>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST

codesign --force --sign - $APP

mkdir -p ~/Applications
pkill -x Lumi 2>/dev/null || true
rm -rf ~/Applications/Lumi.app
cp -R $APP ~/Applications/
touch ~/Applications/Lumi.app
echo "Installed ~/Applications/Lumi.app"
