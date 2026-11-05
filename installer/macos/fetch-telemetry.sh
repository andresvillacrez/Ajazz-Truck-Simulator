#!/bin/bash
# Descarga el plugin de telemetria SCS para macOS (truckermudgeon v1.12.1).
# Uso: ./fetch-telemetry.sh [destino]
# Por defecto: .cache/libscs-telemetry.dylib

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
DEST="${1:-$HERE/.cache/libscs-telemetry.dylib}"
URL="https://github.com/truckermudgeon/scs-sdk-plugin/releases/download/v1.12.1/build-macos-latest.zip"
SHA256="19da992a8ef48d1fc24e5af7c308702b7d147174986621290eb0589bd1863ede"
TMP="$(mktemp -t scs-telemetry)"
trap 'rm -f "$TMP" "$TMP.zip"' EXIT

mkdir -p "$(dirname "$DEST")"

if [ -f "$DEST" ]; then
  GOT="$(shasum -a 256 "$DEST" | awk '{print $1}')"
  if [ "$GOT" = "$SHA256" ]; then
    echo "Telemetria ya presente: $DEST"
    exit 0
  fi
fi

echo "Descargando telemetria macOS..."
curl -L --fail --silent --show-error -o "$TMP.zip" "$URL"
GOT="$(shasum -a 256 "$TMP.zip" | awk '{print $1}')"
if [ "$GOT" != "$SHA256" ]; then
  echo "ERROR: hash del zip no coincide." >&2
  echo "  esperado: $SHA256" >&2
  echo "  obtenido: $GOT" >&2
  exit 1
fi

unzip -p "$TMP.zip" libscs-telemetry.dylib > "$TMP"
mv "$TMP" "$DEST"
chmod 755 "$DEST"
echo "Guardado: $DEST"
shasum -a 256 "$DEST"
