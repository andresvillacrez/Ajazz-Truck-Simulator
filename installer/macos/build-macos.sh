#!/bin/bash
# Compila los instaladores macOS del plugin Trucker Simulator.
# Debe ejecutarse en macOS (usa ditto, pkgbuild y shasum).
#
# Uso:
#   ./build-macos.sh
#   TELEMETRY=/ruta/libscs-telemetry.dylib ./build-macos.sh
#
# Salida en installer/Output/:
#   TruckerSim-Ajazz-macOS-1.0-beta.zip
#   TruckerSim-Ajazz-macOS-1.0-beta.pkg   (si pkgbuild esta disponible)
#   SHA256SUMS-macOS.txt

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
PLUGIN="$ROOT/plugins/com.hsgaming.scsmon.sdPlugin"
OUT="$ROOT/installer/Output"
VERSION="1.0-beta"
PKG_VERSION="1.0"
NAME="TruckerSim-Ajazz-macOS-$VERSION"
STAGE="$OUT/macos-stage/$NAME"
TELEMETRY="${TELEMETRY:-$HERE/.cache/libscs-telemetry.dylib}"

if [ "$(uname -s)" != "Darwin" ]; then
  echo "ERROR: build-macos.sh debe ejecutarse en macOS." >&2
  exit 1
fi

[ -f "$PLUGIN/backend" ] || {
  echo "ERROR: falta $PLUGIN/backend" >&2
  exit 1
}
[ -f "$HERE/manifest-macos.json" ] || {
  echo "ERROR: falta installer/macos/manifest-macos.json" >&2
  exit 1
}

if [ ! -f "$TELEMETRY" ]; then
  echo "Telemetria no encontrada; se descarga con fetch-telemetry.sh..."
  bash "$HERE/fetch-telemetry.sh" "$TELEMETRY"
fi
[ -f "$TELEMETRY" ] || {
  echo "ERROR: falta la telemetria $TELEMETRY" >&2
  exit 1
}

mkdir -p "$OUT"
rm -rf "$STAGE"
mkdir -p "$STAGE/payload/com.hsgaming.scsmon.sdPlugin/propertyinspector"

# Plugin: solo archivos de ejecucion, no fuentes.
cp "$PLUGIN/backend" "$STAGE/payload/com.hsgaming.scsmon.sdPlugin/backend"
cp "$HERE/manifest-macos.json" "$STAGE/payload/com.hsgaming.scsmon.sdPlugin/manifest.json"
cp "$PLUGIN"/*.png "$STAGE/payload/com.hsgaming.scsmon.sdPlugin/"
cp "$PLUGIN"/propertyinspector/* "$STAGE/payload/com.hsgaming.scsmon.sdPlugin/propertyinspector/"
chmod +x "$STAGE/payload/com.hsgaming.scsmon.sdPlugin/backend"

# Telemetria y licencias.
cp "$TELEMETRY" "$STAGE/payload/libscs-telemetry.dylib"
chmod 755 "$STAGE/payload/libscs-telemetry.dylib"
cp "$ROOT/LICENSE" "$STAGE/LICENCIA-MIT.txt"
cp "$ROOT/docs/scs-sdk-plugin-MIT.txt" "$STAGE/scs-sdk-plugin-MIT.txt"
cp "$HERE/LEEME-macos.txt" "$STAGE/LEEME.txt"

# Instaladores de doble clic.
cp "$HERE/install-macos.command" "$STAGE/Instalar Trucker Simulator.command"
cp "$HERE/uninstall-macos.command" "$STAGE/Desinstalar Trucker Simulator.command"
chmod +x "$STAGE/Instalar Trucker Simulator.command" "$STAGE/Desinstalar Trucker Simulator.command"

# ZIP listo para compartir. ditto conserva permisos y recursos.
rm -f "$OUT/$NAME.zip"
(cd "$OUT/macos-stage" && ditto -c -k --keepParent "$NAME" "$OUT/$NAME.zip")

# PKG nativo (component package sin firma). Util para instalar con doble clic
# sin Terminal; macOS puede pedir clic derecho > Abrir por no estar firmado.
if command -v pkgbuild >/dev/null 2>&1; then
  PKG_ROOT="$OUT/macos-pkg-root"
  PKG_SCRIPTS="$OUT/macos-pkg-scripts"
  rm -rf "$PKG_ROOT" "$PKG_SCRIPTS"
  mkdir -p "$PKG_ROOT/private/tmp/truckersim-ajazz-payload" "$PKG_SCRIPTS"

  cp -R "$STAGE/payload/com.hsgaming.scsmon.sdPlugin" "$PKG_ROOT/private/tmp/truckersim-ajazz-payload/"
  cp "$TELEMETRY" "$PKG_ROOT/private/tmp/truckersim-ajazz-payload/libscs-telemetry.dylib"
  cp "$HERE/pkg-postinstall" "$PKG_SCRIPTS/postinstall"
  chmod +x "$PKG_SCRIPTS/postinstall"

  rm -f "$OUT/$NAME.pkg"
  pkgbuild \
    --root "$PKG_ROOT" \
    --scripts "$PKG_SCRIPTS" \
    --identifier "com.hsgaming.truckersim.installer" \
    --version "$PKG_VERSION" \
    --install-location "/" \
    "$OUT/$NAME.pkg"
fi

# Hashes de los artefactos.
(
  cd "$OUT"
  {
    [ -f "$NAME.zip" ] && shasum -a 256 "$NAME.zip"
    [ -f "$NAME.pkg" ] && shasum -a 256 "$NAME.pkg"
  } > SHA256SUMS-macOS.txt
)

echo ""
echo "Artefactos generados en $OUT:"
ls -lh "$OUT/$NAME.zip" "$OUT/$NAME.pkg" 2>/dev/null || ls -lh "$OUT/$NAME.zip"
echo ""
cat "$OUT/SHA256SUMS-macOS.txt"
