# Port Linux / OpenDeck

Integración experimental de Trucker Simulator con **OpenDeck** en Linux.

## Arquitectura

ATS/ETS2 no tienen versión nativa Linux: corren bajo **Proton**. El plugin
`scs-telemetry.dll` se carga dentro del juego y escribe la telemetría en memoria
compartida Windows (`Local\SCSTelemetry`) dentro del prefijo de Proton.

El puente de este port es:

```text
Juego (Proton)
  └─ scs-telemetry.dll
       └─ memoria compartida Windows (prefijo Proton)
            └─ backend.exe (Wine, mismo WINEPREFIX)
                 └─ WebSocket localhost
                      └─ OpenDeck
```

`backend-linux.sh` detecta el prefijo de Proton del juego (o usa
`SCS_WINEPREFIX`), ejecuta `backend.exe` con Wine y le pasa los argumentos que
OpenDeck usa para los plugins (`-port`, `-pluginUUID`, `-registerEvent`).

## Requisitos

- Linux x86_64 con OpenDeck instalado (nativo o Flatpak).
- **Wine** (`wine64` o `wine`) con **wine-mono** para ejecutar `backend.exe`
  (.NET Framework 4).
- ATS y/o ETS2 bajo Proton, con `scs-telemetry.dll` en
  `<juego>/bin/win_x64/plugins/`.
- El juego abierto al menos una vez para que exista el prefijo.

## Compilar

Desde Linux:

```bash
cd ports/linux-opendeck
./build-linux.sh
```

El script necesita `backend.exe` en
`plugins/com.hsgaming.scsmon.sdPlugin/` (compílalo en Windows con
`ports/windows/build-windows.ps1`) y genera:

```text
dist/com.hsgaming.scsmon.sdPlugin/
dist/TruckerSim-Ajazz-Linux-OpenDeck-1.1.tar.gz
```

## Instalar

```bash
./install-linux.sh
```

Instala en:

- `~/.config/opendeck/plugins/` (OpenDeck nativo), o
- `~/.var/app/me.amankhanna.opendeck/config/opendeck/plugins/` (Flatpak).

También puedes definir `OPENDECK_CONFIG` para una ruta personalizada.

## Prefijo de Proton

El wrapper busca, en este orden:

1. `SCS_WINEPREFIX`.
2. Variables `WINEPREFIX`/`STEAM_COMPAT_DATA_PATH` del proceso del juego.
3. `compatdata/270880/pfx` (ATS) y `compatdata/227300/pfx` (ETS2) en las
   bibliotecas de Steam, incluyendo Flatpak.

Para forzarlo:

```bash
export SCS_WINEPREFIX="$HOME/.steam/steam/steamapps/compatdata/270880/pfx"
```

## Limitaciones actuales

- **Wine + wine-mono** son obligatorios. Sin ellos `backend.exe` no arranca.
  Si el render falla por GDI+, prueba `winetricks gdiplus` en el prefijo.
- La inyección de teclas usa `SendInput` de Wine. En X11 suele funcionar con el
  juego enfocado; en Wayland puede requerir `uinput` o `ydotool`.
- OpenDeck Flatpak puede no heredar el entorno de Wine; en ese caso usa
  OpenDeck nativo o define `SCS_WINEPREFIX` dentro del wrapper.
- El rendimiento del puente Wine es inferior al backend nativo de Windows.
- Es una **v0 experimental**: no sustituye todavía al backend nativo.

## Diagnóstico

- Log del plugin: `~/.local/share/opendeck/logs/com.hsgaming.scsmon.sdPlugin.log`
- Log de OpenDeck: `~/.local/share/opendeck/logs/`
- Comprueba el prefijo:
  ```bash
  ls "$SCS_WINEPREFIX/drive_c"
  wine64 --version
  ```
- Prueba el backend manualmente con el juego abierto:
  ```bash
  WINEPREFIX="$SCS_WINEPREFIX" wine64 backend.exe -Test
  ```

## Roadmap

1. Validar telemetría y teclas en ETS2/ATS con Proton.
2. Añadir detección de Flatpak y un instalador `.sh` más guiado.
3. Evaluar `uinput`/`ydotool` para Wayland.
4. Backend Linux nativo (Python/Rust) leyendo telemetría desde un socket.
5. Paquete `.deb`/`.rpm`/AppImage con OpenDeck y dependencias.

Ver `../MEJORAS.md` y `src/README.md`.
