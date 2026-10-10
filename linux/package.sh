#!/bin/bash
# Builds dist/Lumi-x86_64.AppImage: a self-contained single-file build wrapped with appimagetool.
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION=${APP_VERSION:-0.0.0}

dotnet publish linux/Lumi/Lumi.csproj -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true \
  -p:Version="$VERSION" -o dist/linux

APPDIR=dist/Lumi.AppDir
rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin"
cp dist/linux/lumi "$APPDIR/usr/bin/lumi"
cp linux/appimage/AppRun linux/appimage/lumi.desktop "$APPDIR/"
cp docs/brand/avatar.png "$APPDIR/lumi.png"

if [ ! -x dist/appimagetool ]; then
  curl -fsSL -o dist/appimagetool https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
  chmod +x dist/appimagetool
fi
ARCH=x86_64 dist/appimagetool --appimage-extract-and-run "$APPDIR" dist/Lumi-x86_64.AppImage
echo "Built dist/Lumi-x86_64.AppImage"
