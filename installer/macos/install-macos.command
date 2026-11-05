#!/bin/bash
# Trucker Simulator para Ajazz StreamDock - instalador macOS
# Uso: doble clic o `bash "Instalar Trucker Simulator.command"`.
# Instala el plugin en el perfil del usuario y la telemetria en ATS/ETS2.

set -u

HERE="$(cd "$(dirname "$0")" && pwd)"
PAYLOAD="$HERE/payload"
PLUGIN_SRC="$PAYLOAD/com.hsgaming.scsmon.sdPlugin"
TELEMETRY="$PAYLOAD/libscs-telemetry.dylib"
APP_SUPPORT="$HOME/Library/Application Support"
STREAMDOCK_ROOT="$APP_SUPPORT/HotSpot/StreamDock"
PLUGIN_DIR="$STREAMDOCK_ROOT/plugins"
TARGET="$PLUGIN_DIR/com.hsgaming.scsmon.sdPlugin"

pause() {
  if [ -t 0 ]; then
    printf '\nPulsa Enter para cerrar...'
    read -r _
  fi
}

fail() {
  printf '\nERROR: %s\n' "$*" >&2
  pause
  exit 1
}

say() { printf '%s\n' "$*"; }

say "============================================================"
say " Trucker Simulator para Ajazz StreamDock - macOS"
say "============================================================"
say ""

[ -d "$PLUGIN_SRC" ] || fail "No encuentro payload/com.hsgaming.scsmon.sdPlugin."
[ -f "$TELEMETRY" ] || fail "No encuentro payload/libscs-telemetry.dylib."
[ -x /usr/bin/sw_vers ] || fail "Este instalador solo funciona en macOS."

if [ "$(id -u)" -eq 0 ]; then
  fail "No ejecutes este instalador con sudo. Debe instalarse en tu usuario."
fi

say "Plugin:    $TARGET"
say "Telemetria: $TELEMETRY"
say ""

# Cerrar Stream Dock para poder reemplazar el backend.
if pgrep -x "Stream Dock" >/dev/null 2>&1; then
  say "Cerrando Stream Dock..."
  osascript -e 'tell application "Stream Dock" to quit' >/dev/null 2>&1 || true
  sleep 1
fi
pkill -f "Stream Dock.app/Contents/MacOS" >/dev/null 2>&1 || true
pkill -f "StreamDock.app/Contents/MacOS" >/dev/null 2>&1 || true
pkill -f "com.hsgaming.scsmon.sdPlugin/backend" >/dev/null 2>&1 || true

mkdir -p "$PLUGIN_DIR" || fail "No puedo crear $PLUGIN_DIR"

if [ -d "$TARGET" ]; then
  BACKUP="$TARGET.backup-$(date +%Y%m%d-%H%M%S)"
  cp -R "$TARGET" "$BACKUP" || fail "No puedo crear la copia de seguridad."
  say "Copia de seguridad: $BACKUP"
fi

rm -rf "$TARGET" || fail "No puedo reemplazar el plugin instalado."
cp -R "$PLUGIN_SRC" "$TARGET" || fail "No puedo copiar el plugin."
chmod +x "$TARGET/backend" 2>/dev/null || true
xattr -dr com.apple.quarantine "$TARGET" 2>/dev/null || true
say "Plugin instalado."

# Detectar ATS/ETS2 en Steam y en las rutas habituales.
GAME_APPS=()

add_game() {
  local app="$1"
  local existing
  [ -d "$app/Contents/MacOS" ] || return 0
  for existing in "${GAME_APPS[@]:-}"; do
    [ "$existing" = "$app" ] && return 0
  done
  GAME_APPS+=("$app")
}

STEAM="$APP_SUPPORT/Steam"
add_game "$STEAM/steamapps/common/American Truck Simulator/American Truck Simulator.app"
add_game "$STEAM/steamapps/common/Euro Truck Simulator 2/Euro Truck Simulator 2.app"
add_game "/Applications/American Truck Simulator.app"
add_game "/Applications/Euro Truck Simulator 2.app"

VDF="$STEAM/steamapps/libraryfolders.vdf"
if [ -f "$VDF" ]; then
  while IFS= read -r line; do
    case "$line" in
      *'"path"'*)
        LIB="$(printf '%s\n' "$line" | sed -n 's/.*"path"[[:space:]]*"\(.*\)".*/\1/p')"
        if [ -n "$LIB" ]; then
          add_game "$LIB/steamapps/common/American Truck Simulator/American Truck Simulator.app"
          add_game "$LIB/steamapps/common/Euro Truck Simulator 2/Euro Truck Simulator 2.app"
        fi
        ;;
    esac
  done < "$VDF"
fi

INSTALLED=0
if [ "${#GAME_APPS[@]}" -gt 0 ]; then
  say ""
  say "Instalando telemetria en los juegos detectados..."
  for GAME in "${GAME_APPS[@]}"; do
    PLUGINS="$GAME/Contents/MacOS/plugins"
    mkdir -p "$PLUGINS" || continue
    cp "$TELEMETRY" "$PLUGINS/libscs-telemetry.dylib" || continue
    chmod 755 "$PLUGINS/libscs-telemetry.dylib" 2>/dev/null || true
    xattr -d com.apple.quarantine "$PLUGINS/libscs-telemetry.dylib" 2>/dev/null || true
    say "  OK  $GAME"
    INSTALLED=$((INSTALLED + 1))
  done
fi

if [ "$INSTALLED" -eq 0 ]; then
  say ""
  say "No encontre ATS/ETS2 automaticamente."
  say "Copia a mano este archivo:"
  say "  $TELEMETRY"
  say "en la carpeta plugins de cada juego:"
  say "  .../American Truck Simulator.app/Contents/MacOS/plugins/"
  say "  .../Euro Truck Simulator 2.app/Contents/MacOS/plugins/"
  say ""
  say "Crea la carpeta plugins si no existe."
fi

say ""
say "============================================================"
say " Instalacion completada"
say "============================================================"
say ""
say "1. Abre Stream Dock. La categoria Trucker Simulator debe aparecer."
say "2. Arrastra las acciones a las teclas."
say "3. Abre ATS/ETS2 y comprueba que el plugin recibe datos."
say ""
say "macOS pedira permiso de Accesibilidad para enviar teclas."
say "Activalo en Ajustes del Sistema > Privacidad y seguridad >"
say "Accesibilidad para Stream Dock (o para backend)."
say ""
say "Esta version macOS es beta: incluye las 21 acciones de la v1.0."
say "Las acciones nuevas de la v1.1 de Windows aun no estan portadas."
say ""
pause
