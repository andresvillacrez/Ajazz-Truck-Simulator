#!/usr/bin/env bash
# Puente Linux/OpenDeck para Trucker Simulator.
#
# OpenDeck lanza este script con los mismos argumentos que StreamDock:
#   -port <puerto> -pluginUUID <uuid> -registerEvent <evento>
#
# El script ejecuta el backend Windows (backend.exe) con Wine dentro del
# prefijo de Proton del juego, para que pueda leer la memoria compartida
# Local\SCSTelemetry que escribe scs-telemetry.dll.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
BACKEND="$HERE/backend.exe"
LOG_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/opendeck/logs"
LOG="$LOG_DIR/com.hsgaming.scsmon.sdPlugin.log"
mkdir -p "$LOG_DIR"

log() {
  printf '[scsmon-linux] %s\n' "$*" | tee -a "$LOG" >&2
}

if [ ! -f "$BACKEND" ]; then
  log "ERROR: no se encontró $BACKEND"
  exit 1
fi

WINE=""
for candidate in wine64 wine; do
  if command -v "$candidate" >/dev/null 2>&1; then
    WINE="$candidate"
    break
  fi
done
if [ -z "$WINE" ]; then
  log "ERROR: Wine no está instalado. Instala wine64 y wine-mono."
  exit 1
fi

prefix_from_process() {
  local pid env prefix
  for pid in $(pgrep -f 'amtrucks\.exe|eurotrucks2\.exe' 2>/dev/null || true); do
    env="/proc/$pid/environ"
    [ -r "$env" ] || continue

    prefix="$(tr '\0' '\n' < "$env" | sed -n 's/^WINEPREFIX=//p' | head -n 1)"
    if [ -n "$prefix" ] && [ -d "$prefix" ]; then
      printf '%s\n' "$prefix"
      return 0
    fi

    prefix="$(tr '\0' '\n' < "$env" | sed -n 's/^STEAM_COMPAT_DATA_PATH=//p' | head -n 1)"
    if [ -n "$prefix" ] && [ -d "$prefix/pfx" ]; then
      printf '%s\n' "$prefix/pfx"
      return 0
    fi
  done
  return 1
}

prefix_from_steam_libraries() {
  local vdf lib app candidate
  local -a vdfs=(
    "$HOME/.steam/steam/steamapps/libraryfolders.vdf"
    "$HOME/.local/share/Steam/steamapps/libraryfolders.vdf"
    "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/libraryfolders.vdf"
  )

  for vdf in "${vdfs[@]}"; do
    [ -f "$vdf" ] || continue
    while IFS= read -r lib; do
      [ -n "$lib" ] || continue
      for app in 270880 227300; do
        candidate="$lib/steamapps/compatdata/$app/pfx"
        if [ -d "$candidate" ]; then
          printf '%s\n' "$candidate"
          return 0
        fi
      done
    done < <(grep -oP '"path"\s+"\K[^"]+' "$vdf" 2>/dev/null || true)
  done

  local -a common=(
    "$HOME/.steam/steam/steamapps/compatdata/270880/pfx"
    "$HOME/.steam/steam/steamapps/compatdata/227300/pfx"
    "$HOME/.local/share/Steam/steamapps/compatdata/270880/pfx"
    "$HOME/.local/share/Steam/steamapps/compatdata/227300/pfx"
    "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/compatdata/270880/pfx"
    "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/compatdata/227300/pfx"
  )
  for candidate in "${common[@]}"; do
    if [ -d "$candidate" ]; then
      printf '%s\n' "$candidate"
      return 0
    fi
  done

  return 1
}

PREFIX="${SCS_WINEPREFIX:-${WINEPREFIX:-}}"
if [ -z "$PREFIX" ] || [ ! -d "$PREFIX" ]; then
  PREFIX="$(prefix_from_process || true)"
fi
if [ -z "$PREFIX" ] || [ ! -d "$PREFIX" ]; then
  PREFIX="$(prefix_from_steam_libraries || true)"
fi

if [ -z "$PREFIX" ] || [ ! -d "$PREFIX" ]; then
  log "ERROR: no se encontró el prefijo de Proton de ATS/ETS2."
  log "Abre el juego al menos una vez o define SCS_WINEPREFIX=/ruta/al/pfx."
  log "Rutas típicas: ~/.steam/steam/steamapps/compatdata/270880/pfx (ATS)"
  log "                 ~/.steam/steam/steamapps/compatdata/227300/pfx (ETS2)"
  exit 1
fi

if [ ! -d "$PREFIX/drive_c" ]; then
  log "AVISO: $PREFIX no parece un prefijo Wine válido (falta drive_c)."
fi

log "Wine: $WINE"
log "Prefijo: $PREFIX"
log "Backend: $BACKEND"
log "Argumentos: $*"

export WINEPREFIX="$PREFIX"
export WINEDEBUG="${WINEDEBUG:--all}"

# OpenDeck captura stdout/stderr de este proceso; el backend escribe su log
# propio en %TEMP% dentro del prefijo.
exec "$WINE" "$BACKEND" "$@"
