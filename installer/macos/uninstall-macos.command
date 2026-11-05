#!/bin/bash
# Desinstalador macOS - Trucker Simulator para Ajazz StreamDock
# Elimina el plugin del usuario y, opcionalmente, la telemetria de ATS/ETS2.

set -u

HERE="$(cd "$(dirname "$0")" && pwd)"
APP_SUPPORT="$HOME/Library/Application Support"
PLUGIN_DIR="$APP_SUPPORT/HotSpot/StreamDock/plugins"
TARGET="$PLUGIN_DIR/com.hsgaming.scsmon.sdPlugin"
STEAM="$APP_SUPPORT/Steam"

say() { printf '%s\n' "$*"; }

say "============================================================"
say " Desinstalar Trucker Simulator - macOS"
say "============================================================"
say ""

if [ "$(id -u)" -eq 0 ]; then
  say "No ejecutes este desinstalador con sudo."
  exit 1
fi

if pgrep -x "Stream Dock" >/dev/null 2>&1; then
  say "Cerrando Stream Dock..."
  osascript -e 'tell application "Stream Dock" to quit' >/dev/null 2>&1 || true
  sleep 1
fi
pkill -f "Stream Dock.app/Contents/MacOS" >/dev/null 2>&1 || true
pkill -f "StreamDock.app/Contents/MacOS" >/dev/null 2>&1 || true
pkill -f "com.hsgaming.scsmon.sdPlugin/backend" >/dev/null 2>&1 || true

if [ -d "$TARGET" ]; then
  rm -rf "$TARGET"
  say "Plugin eliminado: $TARGET"
else
  say "El plugin no estaba instalado en $TARGET."
fi

say ""
say "Las copias de seguridad del plugin no se borran."
say ""
printf 'Â¿Quitar tambien libscs-telemetry.dylib de ATS/ETS2? [s/N] '
read -r ANSWER
case "$ANSWER" in
  s|S|si|SI|sÃ­|SÃ)
    GAME_APPS=(
      "$STEAM/steamapps/common/American Truck Simulator/American Truck Simulator.app"
      "$STEAM/steamapps/common/Euro Truck Simulator 2/Euro Truck Simulator 2.app"
      "/Applications/American Truck Simulator.app"
      "/Applications/Euro Truck Simulator 2.app"
    )
    for GAME in "${GAME_APPS[@]}"; do
      LIB="$GAME/Contents/MacOS/plugins/libscs-telemetry.dylib"
      if [ -f "$LIB" ]; then
        rm -f "$LIB"
        say "Eliminado: $LIB"
      fi
    done
    ;;
  *)
    say "Se conserva la telemetria. Otras apps pueden seguir usandola."
    ;;
esac

say ""
say "Listo. Abre Stream Dock para actualizar la lista de acciones."
say ""
if [ -t 0 ]; then
  printf 'Pulsa Enter para cerrar...'
  read -r _
fi
