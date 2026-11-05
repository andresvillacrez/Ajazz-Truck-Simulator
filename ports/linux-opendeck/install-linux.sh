#!/usr/bin/env bash
# Instala el plugin Linux/OpenDeck en el perfil del usuario.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
STAGE="$HERE/dist/com.hsgaming.scsmon.sdPlugin"

if [ ! -d "$STAGE" ]; then
  echo "No existe $STAGE. Ejecuta primero ./build-linux.sh" >&2
  exit 1
fi

if [ -n "${OPENDECK_CONFIG:-}" ]; then
  CONFIG="$OPENDECK_CONFIG"
elif [ -d "$HOME/.var/app/me.amankhanna.opendeck/config/opendeck" ]; then
  CONFIG="$HOME/.var/app/me.amankhanna.opendeck/config/opendeck"
  echo "Detectado OpenDeck Flatpak."
else
  CONFIG="${XDG_CONFIG_HOME:-$HOME/.config}/opendeck"
fi

PLUGINS="$CONFIG/plugins"
TARGET="$PLUGINS/com.hsgaming.scsmon.sdPlugin"

mkdir -p "$PLUGINS"

if [ -d "$TARGET" ]; then
  BACKUP="$TARGET.backup-$(date +%Y%m%d-%H%M%S)"
  cp -a "$TARGET" "$BACKUP"
  echo "Copia de seguridad: $BACKUP"
  rm -rf "$TARGET"
fi

cp -a "$STAGE" "$TARGET"
chmod +x "$TARGET/backend-linux.sh"

echo "Plugin instalado en: $TARGET"
echo

if ! command -v wine64 >/dev/null 2>&1 && ! command -v wine >/dev/null 2>&1; then
  echo "AVISO: Wine no está instalado. Instala wine64 y wine-mono para usar el puente."
fi

if ! command -v protontricks >/dev/null 2>&1; then
  echo "Sugerencia: protontricks puede ayudar a inspeccionar el prefijo de Proton."
fi

cat <<'EOF'

Siguientes pasos:
1. Asegúrate de que scs-telemetry.dll está instalado en ATS/ETS2.
   Puedes usar el instalador de Windows o copiarlo a:
   <juego>/bin/win_x64/plugins/scs-telemetry.dll
2. Abre el juego al menos una vez para que Proton cree el prefijo.
3. Reinicia OpenDeck. El plugin aparecerá como "Trucker Simulator".
4. Si el backend no encuentra la telemetría, define el prefijo manualmente:
   SCS_WINEPREFIX="$HOME/.steam/steam/steamapps/compatdata/270880/pfx"
5. En Wayland, la inyección de teclas puede requerir uinput/ydotool.
EOF
