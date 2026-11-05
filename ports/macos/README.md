# Port macOS

Backend nativo Swift para StreamDock / Ajazz en macOS.

## Estado

- Versión beta: **1.0-beta**, 21 acciones (faltan 11 respecto a Windows 1.1).
- Backend: `src/backend.swift`.
- Empaquetado existente: `../../installer/macos/build-macos.sh`.
- Telemetría: `libscs-telemetry.dylib` (se descarga con
  `installer/macos/fetch-telemetry.sh`).

## Compilar

Requisitos: macOS con Xcode Command Line Tools (`swiftc`, `lipo`, `ditto`,
`pkgbuild`).

```bash
./build-macos.sh
```

El script:

1. Compila `backend.swift` para `x86_64` y `arm64` (si el toolchain lo
   soporta).
2. Genera un binario universal con `lipo` en la carpeta compartida del plugin.
3. Ejecuta `installer/macos/build-macos.sh` para producir el ZIP/PKG beta.

Compilación manual:

```bash
swiftc -O -target x86_64-apple-macos10.13 \
  -o ../../plugins/com.hsgaming.scsmon.sdPlugin/backend \
  src/backend.swift
```

## Instalar en desarrollo

El instalador copia el plugin a:

```text
~/Library/Application Support/HotSpot/StreamDock/plugins/com.hsgaming.scsmon.sdPlugin/
```

y la telemetría a:

```text
<ATS o ETS2>.app/Contents/MacOS/plugins/libscs-telemetry.dylib
```

Concede **Accesibilidad** a StreamDock/backend para que los botones puedan
enviar teclas.

## Mejoras pendientes

- Completar las 11 acciones que faltan y las ventanillas automáticas.
- Binario universal `arm64` + `x86_64` (hoy x86_64 y Rosetta 2).
- Firmar y notarizar el `.pkg` para distribución pública.
- Detectar y guiar al usuario si falta el permiso de Accesibilidad.
- Igualar el instalador al de Windows: accesos directos/desinstalador.
- Pruebas de snapshot de tiles.

Ver `../MEJORAS.md`.
