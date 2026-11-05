#!/bin/bash
# Compila el backend macOS (x86_64 + arm64 si el toolchain lo permite).
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
PLUGIN="$ROOT/plugins/com.hsgaming.scsmon.sdPlugin"
SOURCE="$HERE/src/backend.swift"
OUTPUT="$PLUGIN/backend"
BUILD="$HERE/.build"

if [ "$(uname -s)" != "Darwin" ]; then
  echo "ERROR: build-macos.sh debe ejecutarse en macOS." >&2
  exit 1
fi

command -v swiftc >/dev/null 2>&1 || {
  echo "ERROR: falta swiftc (Xcode Command Line Tools)." >&2
  exit 1
}

mkdir -p "$BUILD"
rm -f "$BUILD/backend-x86_64" "$BUILD/backend-arm64" "$OUTPUT"

echo "Compilando x86_64..."
swiftc -O -target x86_64-apple-macos10.13 \
  -o "$BUILD/backend-x86_64" "$SOURCE"

echo "Compilando arm64..."
if swiftc -O -target arm64-apple-macos11.0 \
  -o "$BUILD/backend-arm64" "$SOURCE" 2>/dev/null; then
  if command -v lipo >/dev/null 2>&1; then
    lipo -create "$BUILD/backend-x86_64" "$BUILD/backend-arm64" -output "$OUTPUT"
    echo "Binario universal creado."
  else
    cp "$BUILD/backend-x86_64" "$OUTPUT"
    echo "AVISO: lipo no disponible; se usa x86_64."
  fi
else
  echo "AVISO: no se pudo compilar arm64; se usa x86_64."
  cp "$BUILD/backend-x86_64" "$OUTPUT"
fi

chmod +x "$OUTPUT"
echo "OK: $OUTPUT"

echo "Empaquetando con installer/macos/build-macos.sh..."
bash "$ROOT/installer/macos/build-macos.sh"
