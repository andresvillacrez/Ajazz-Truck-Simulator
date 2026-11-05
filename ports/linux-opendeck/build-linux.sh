#!/usr/bin/env bash
# Ensambla el plugin Linux/OpenDeck a partir de los recursos compartidos.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
SHARED="$ROOT/plugins/com.hsgaming.scsmon.sdPlugin"
DIST="$HERE/dist"
STAGE="$DIST/com.hsgaming.scsmon.sdPlugin"
VERSION="1.1"

[ -f "$HERE/manifest.linux.json" ] || {
  echo "ERROR: falta ports/linux-opendeck/manifest.linux.json" >&2
  exit 1
}

[ -f "$SHARED/backend.exe" ] || {
  echo "ERROR: falta $SHARED/backend.exe" >&2
  echo "Compílalo desde ports/windows/build-windows.ps1 o usa el asset de release." >&2
  exit 1
}

rm -rf "$STAGE"
mkdir -p "$STAGE/propertyinspector"

cp "$HERE/manifest.linux.json" "$STAGE/manifest.json"
cp "$HERE/backend-linux.sh" "$STAGE/backend-linux.sh"
cp "$SHARED/backend.exe" "$STAGE/backend.exe"
cp "$SHARED"/*.png "$STAGE/"
cp "$SHARED"/propertyinspector/* "$STAGE/propertyinspector/"

cp "$ROOT/LICENSE" "$DIST/LICENCIA-MIT.txt"
cp "$ROOT/docs/scs-sdk-plugin-MIT.txt" "$DIST/scs-sdk-plugin-MIT.txt"
cp "$HERE/README.md" "$DIST/LEEME-linux.md"
cp "$HERE/install-linux.sh" "$DIST/install-linux.sh"

chmod +x "$STAGE/backend-linux.sh" "$DIST/install-linux.sh"

TAR="$DIST/TruckerSim-Ajazz-Linux-OpenDeck-$VERSION.tar.gz"
rm -f "$TAR"
tar -czf "$TAR" -C "$DIST" "com.hsgaming.scsmon.sdPlugin"

echo "Plugin: $STAGE"
echo "Paquete: $TAR"
