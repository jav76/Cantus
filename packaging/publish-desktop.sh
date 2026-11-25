#!/bin/bash
# Publishes the desktop bundle: the app, its own server, and one private .NET runtime both run on.
#
#   <output>/Cantus[.exe]                  the app, which looks for ./dotnet
#   <output>/server/Cantus.Server[.exe]    the server, which looks for ../dotnet
#   <output>/dotnet/                       host/fxr and shared/<framework>/<version>
#
# Usage: publish-desktop.sh <rid> <version> <output-dir>
set -euo pipefail

if [ $# -ne 3 ]; then
    echo "Usage: $0 <rid> <version> <output-dir>" >&2
    exit 1
fi

RID="$1"
VERSION="$2"
OUTPUT_DIR="$3"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
CLIENT_PROJECT="${REPO_ROOT}/src/Cantus.Client/Cantus.Client/Cantus.Client.csproj"
SERVER_PROJECT="${REPO_ROOT}/src/Cantus.Server/Cantus.Server.csproj"

mkdir -p "${OUTPUT_DIR}"

# MSBuild resolves a relative property path against the project, and on Windows needs a
# Windows-style path, which only Git Bash's pwd -W gives.
if OUTPUT_ABS="$(cd "${OUTPUT_DIR}" && pwd -W 2>/dev/null)"; then
    :
else
    OUTPUT_ABS="$(cd "${OUTPUT_DIR}" && pwd)"
fi

COMMON_ARGS=(
    -r "${RID}"
    -c Release
    -p:SelfContained=false
    -p:PublishSingleFile=true
    -p:Version="${VERSION}"
    -p:SourceRevisionId="${SOURCE_REVISION_ID:-}"
)

echo "==> Publishing the desktop app (${RID})"
dotnet publish "${CLIENT_PROJECT}" -f net10.0-desktop "${COMMON_ARGS[@]}" \
    -p:AppHostRelativeDotNet=./dotnet \
    -o "${OUTPUT_ABS}"

echo "==> Publishing the bundled server (${RID})"
dotnet publish "${SERVER_PROJECT}" "${COMMON_ARGS[@]}" \
    -p:AppHostRelativeDotNet=../dotnet \
    -o "${OUTPUT_ABS}/server"

echo "==> Laying out the shared .NET runtime (${RID})"
dotnet msbuild "${SERVER_PROJECT}" -restore -nologo -v:minimal \
    -t:AssembleSharedRuntime \
    -p:RuntimeIdentifier="${RID}" \
    -p:SelfContained=true \
    -p:SharedRuntimeDir="${OUTPUT_ABS}/dotnet"

# Debug symbols are most of the download (SkiaSharp's native ones alone are about 80 MB).
find "${OUTPUT_DIR}" -name '*.pdb' -type f -delete
rm -f "${OUTPUT_DIR}/server/appsettings.Development.json"

echo "==> Desktop bundle ready in ${OUTPUT_DIR}"
