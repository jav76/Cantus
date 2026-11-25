#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"

PUBLISH_EXPLICIT=0
if [ $# -ge 1 ]; then
    PUBLISH_EXPLICIT=1
fi

SOURCE_DIR="${1:-${SCRIPT_DIR}/publish}"
OUTPUT_DIR="${2:-${SCRIPT_DIR}/artifacts}"
VERSION="${3:-1.0.0}"

echo "==> Building Cantus AppImage version: ${VERSION}"
echo "    Source directory: ${SOURCE_DIR}"
echo "    Output directory: ${OUTPUT_DIR}"

# If no custom source directory was explicitly passed as argument, or if binaries don't exist, publish fresh binaries
if [ "${PUBLISH_EXPLICIT}" -eq 0 ] || [ ! -d "${SOURCE_DIR}" ] || [ ! -f "${SOURCE_DIR}/Cantus" ]; then
    echo "==> Running dotnet publish to ensure fresh Release binaries..."
    mkdir -p "${SOURCE_DIR}"
    dotnet publish "${REPO_ROOT}/src/Cantus.Client/Cantus.Client/Cantus.Client.csproj" \
        -f net10.0-desktop \
        -r linux-x64 \
        -c Release \
        -o "${SOURCE_DIR}" \
        --self-contained
fi

mkdir -p "${OUTPUT_DIR}"
APPDIR_TEMP="$(mktemp -d -t cantus-appdir-XXXXXX)"
trap 'rm -rf "${APPDIR_TEMP}"' EXIT

mkdir -p "${APPDIR_TEMP}/usr/bin"
mkdir -p "${APPDIR_TEMP}/usr/share/icons/hicolor/256x256/apps"

# Copy published binaries
cp -r "${SOURCE_DIR}/"* "${APPDIR_TEMP}/usr/bin/"
chmod +x "${APPDIR_TEMP}/usr/bin/Cantus"

# An AppImage unmounts when the app exits, which would take a still-running bundled server with
# it. The AppImage ships the app only; the tar.gz carries the server.
rm -rf "${APPDIR_TEMP}/usr/bin/server" "${APPDIR_TEMP}/usr/bin/dotnet/shared/Microsoft.AspNetCore.App"

# Copy desktop and launcher scripts
cp "${SCRIPT_DIR}/AppRun" "${APPDIR_TEMP}/AppRun"
chmod +x "${APPDIR_TEMP}/AppRun"
cp "${SCRIPT_DIR}/cantus.desktop" "${APPDIR_TEMP}/cantus.desktop"

cp "${REPO_ROOT}/packaging/icons/cantus.png" "${APPDIR_TEMP}/cantus.png"
cp "${APPDIR_TEMP}/cantus.png" "${APPDIR_TEMP}/usr/share/icons/hicolor/256x256/apps/cantus.png"
cp "${APPDIR_TEMP}/cantus.png" "${APPDIR_TEMP}/.DirIcon"

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
