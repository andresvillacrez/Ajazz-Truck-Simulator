# Instaladores

Este directorio contiene los scripts y la salida de los instaladores.

## Windows

- Script: `truckersim-ajazz.iss` (Inno Setup 6).
- Salida esperada: `Output/TruckerSim-Ajazz-Setup-1.2.exe` (pendiente de compilar; requiere ISCC).

Compilar desde la raíz del repositorio:

```bat
"%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" installer\truckersim-ajazz.iss
```

Antes de compilar deben existir:

- `plugins/com.hsgaming.scsmon.sdPlugin/backend.exe` (compilado desde `ports/windows/src/backend.cs`).
- `dll/scs-telemetry.dll` (RenCloud v1.12.x).

El instalador:

- Copia el plugin a `%APPDATA%\HotSpot\StreamDock\plugins\com.hsgaming.scsmon.sdPlugin`.
- Detecta ATS/ETS2 en Steam y permite indicar carpetas manuales.
- Copia `scs-telemetry.dll` a `<juego>\bin\win_x64\plugins`.
- Cierra Stream Dock antes de instalar.
- Incluye desinstalador y no toca los perfiles del usuario.

## macOS

Ver [`macos/README.md`](macos/README.md).

Resumen:

- `macos/build-macos.sh` debe ejecutarse en macOS y genera:
  - `Output/TruckerSim-Ajazz-macOS-1.0-beta.zip`
  - `Output/TruckerSim-Ajazz-macOS-1.0-beta.pkg`
- El ZIP se puede generar desde Windows con el script de empaquetado del
  proyecto; contiene un `Instalar Trucker Simulator.command`.
- El `.pkg` solo se puede compilar en macOS (`pkgbuild`).
- Es una beta: backend x86_64 y 21 acciones (v1.0). Las 11 acciones nuevas de
  Windows 1.2 aún no están portadas a Swift.

## Versiones

Mantener sincronizados:

- `plugins/com.hsgaming.scsmon.sdPlugin/manifest.json` -> `Version`.
- `CHANGELOG.md`.
- `installer/truckersim-ajazz.iss` -> `MyAppVersion`.
- `installer/macos/build-macos.sh` -> `VERSION` y `PKG_VERSION`.

La versión macOS se mantiene en `1.0-beta` mientras el backend Swift no tenga
paridad con Windows 1.2.

## Firma

Los artefactos no están firmados:

- Windows: SmartScreen puede mostrar un aviso. Para evitarlo hace falta un
  certificado de firma de código.
- macOS: Gatekeeper puede pedir clic derecho > Abrir. Para evitarlo hace falta
  un certificado Apple Developer y notarización.

No se incluyen certificados ni contraseñas en el repositorio.
