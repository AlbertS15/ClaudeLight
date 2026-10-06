#!/bin/zsh
# Builds ClaudeLight.app and installs it into ~/Applications.
set -euo pipefail
cd "${0:A:h}"

APP=build/ClaudeLight.app
rm -rf build
mkdir -p $APP/Contents/MacOS $APP/Contents/Resources

swiftc -O -swift-version 5 -target arm64-apple-macos14.0 \
  -framework AppKit -framework SwiftUI -framework Carbon -framework ServiceManagement \
  Sources/main.swift -o $APP/Contents/MacOS/ClaudeLight

swift Icon/make_icon.swift build/AppIcon.iconset
iconutil -c icns build/AppIcon.iconset -o $APP/Contents/Resources/AppIcon.icns

cat > $APP/Contents/Info.plist <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>ClaudeLight</string>
  <key>CFBundleDisplayName</key><string>ClaudeLight</string>
  <key>CFBundleIdentifier</key><string>io.github.alberts15.claudelight</string>
  <key>CFBundleExecutable</key><string>ClaudeLight</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>1.0</string>
  <key>CFBundleVersion</key><string>1</string>
  <key>LSMinimumSystemVersion</key><string>14.0</string>
  <key>LSUIElement</key><true/>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST

codesign --force --sign - $APP

mkdir -p ~/Applications
pkill -x ClaudeLight 2>/dev/null || true
rm -rf ~/Applications/ClaudeLight.app
cp -R $APP ~/Applications/
touch ~/Applications/ClaudeLight.app
echo "Installed ~/Applications/ClaudeLight.app"
