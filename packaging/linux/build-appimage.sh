#!/bin/bash
set -euo pipefail

SOURCE_DIR="${1:-./publish}"
OUTPUT_DIR="${2:-./artifacts}"
VERSION="${3:-1.0.0}"

echo "==> Building Cantus AppImage version: ${VERSION}"
echo "    Source directory: ${SOURCE_DIR}"
echo "    Output directory: ${OUTPUT_DIR}"

mkdir -p "${OUTPUT_DIR}"
APPDIR_TEMP="$(mktemp -d -t cantus-appdir-XXXXXX)"
trap 'rm -rf "${APPDIR_TEMP}"' EXIT

mkdir -p "${APPDIR_TEMP}/usr/bin"
mkdir -p "${APPDIR_TEMP}/usr/share/icons/hicolor/256x256/apps"

# Copy published binaries
cp -r "${SOURCE_DIR}/"* "${APPDIR_TEMP}/usr/bin/"
chmod +x "${APPDIR_TEMP}/usr/bin/Cantus.Client"

# Copy desktop and launcher scripts
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cp "${SCRIPT_DIR}/AppRun" "${APPDIR_TEMP}/AppRun"
chmod +x "${APPDIR_TEMP}/AppRun"
cp "${SCRIPT_DIR}/cantus.desktop" "${APPDIR_TEMP}/cantus.desktop"

# Create minimal icon if none exists
if [ ! -f "${APPDIR_TEMP}/cantus.png" ]; then
    # Generate a clean placeholder PNG with base64 if no asset present
    echo "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAACXBIWXMAAAsTAAALEwEAmpwYAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAB0SURBVHgB7dKxCcAwEATBlEvqT4X/cE5gB9iA2YmQe4Lq7szu7t4791z3H+A9AwSMCRiTCRiTCRiTCRiTCRiTCRiTCRiTCRiTCRiTCRiTCRiTCRiTCRiTCRiTCRiTCRiTCRiTCRiTCRiTCRiTCRiTCRiTCXgBH0o8fR2j81AAAAAASUVORK5CYN==" | base64 -d > "${APPDIR_TEMP}/cantus.png"
fi
cp "${APPDIR_TEMP}/cantus.png" "${APPDIR_TEMP}/usr/share/icons/hicolor/256x256/apps/cantus.png"

# Fetch appimagetool if not available locally
if ! command -v appimagetool &> /dev/null; then
    echo "==> Downloading appimagetool..."
    wget -q https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage -O /tmp/appimagetool.AppImage
    chmod +x /tmp/appimagetool.AppImage
    (cd /tmp && ./appimagetool.AppImage --appimage-extract > /dev/null)
    APPIMAGETOOL="/tmp/squashfs-root/AppRun"
else
    APPIMAGETOOL="appimagetool"
fi

# Build AppImage with ARCH=x86_64
echo "==> Packaging AppImage..."
ARCH=x86_64 "${APPIMAGETOOL}" "${APPDIR_TEMP}" "${OUTPUT_DIR}/Cantus-Linux-x64.AppImage"

echo "==> Successfully created: ${OUTPUT_DIR}/Cantus-Linux-x64.AppImage"
