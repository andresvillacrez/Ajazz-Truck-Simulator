# Auditoría y mejoras priorizadas

Estado revisado: 2026-11-04. Backend Windows 1.2 estable, macOS 1.0-beta
y port Linux/OpenDeck experimental.

## P0 — Antes de publicar el port Linux

1. **Definir el puente de telemetría en Linux/Proton.**
   - ATS/ETS2 no tienen versión nativa Linux; corren bajo Proton.
   - El plugin `scs-telemetry.dll` escribe en memoria compartida Windows
     (`Local\SCSTelemetry`) dentro del prefijo de Proton.
   - Un proceso Linux nativo no puede abrir esa memoria directamente.
   - Opción inicial recomendada: ejecutar `backend.exe` con Wine usando el
     **mismo `WINEPREFIX`** que el juego. Es el puente que implementa
     `ports/linux-opendeck/backend-linux.sh`.
   - Opción robusta a medio plazo: DLL puente que reenvíe la telemetría por
     socket Unix/TCP a un backend nativo Linux.

2. **Unificar el manifiesto de acciones.**
   - Hoy `manifest.json` y `installer/macos/manifest-macos.json` duplican
     acciones; cualquier cambio exige editar dos archivos.
   - Generar ambos desde un único `actions.json` y añadir solo las diferencias
     de plataforma (`CodePath`, `CodePathMac`, `CodePathLin`, `OS`).

3. **Paridad macOS.**
   - macOS tiene 21 acciones; Windows 1.2 tiene 35.
   - Faltan las 11 acciones nuevas y las ventanillas automáticas.
   - El binario es x86_64; compilar **universal** (`arm64` + `x86_64`) con
     `lipo` para evitar Rosetta 2.
   - Firmar y notarizar el `.pkg`/`.app` si se distribuye fuera de GitHub.

4. **Versionado único.**
   - Hoy conviven `1.2` (Windows/manifest) y `1.0-beta` (macOS).
   - Usar una versión de producto y un sufijo de canal (`1.2`, `1.2-beta`).
   - Validar en CI que `manifest.json`, `CHANGELOG.md` y el instalador
     coinciden.

5. **Instalador Windows sin admin para la app.**
   - El instalador actual pide admin para todo.
   - Separar: app/plugin en `%LOCALAPPDATA%` sin admin; copia de
     `scs-telemetry.dll` a ATS/ETS2 con elevación puntual.
   - Reduce fricción y permite instalaciones corporativas.

## P1 — Calidad y alcance

6. **Backend Linux nativo (fase 2).**
   - Python + Pillow o Rust + `image` para renderizar los tiles.
   - Leer telemetría desde un socket que publique el puente.
   - Inyectar teclas con `uinput`/`ydotool` o X11/XTest; documentar Wayland.
   - Reutilizar la lógica de acciones y claves del backend C#.

7. **Separar `backend.cs` en módulos.**
   - 112 KB monolíticos: offsets, MMF, render, WebSocket, input, ventanas y
     diagnóstico en un solo archivo.
   - Extraer `Telemetry`, `Rendering`, `StreamDeckProtocol`, `Input`,
     `WindowBindings` y `Diagnostics`.
   - Facilita el port Linux y las pruebas.

8. **Pruebas automatizadas.**
   - Simulador de telemetría (Snap sintético) para renderizar las 34 acciones.
   - Snapshot tests de PNG (hash o comparación por píxeles) por acción/estado.
   - Prueba de protocolo WebSocket con un servidor falso.
   - CI matriz: Windows, macOS, Linux (build + tests).

9. **Empaquetado Linux/OpenDeck.**
   - Instalar en `~/.config/opendeck/plugins/` (o ruta Flatpak
     `~/.var/app/me.amankhanna.opendeck/config/opendeck/plugins/`).
   - Detectar OpenDeck Flatpak vs nativo.
   - Añadir script de desinstalación y actualización.
   - Documentar requisitos: Wine, wine-mono, prefijo de Proton, plugin SCS.

10. **Entrada de teclado en Linux.**
    - `SendInput` de Wine puede funcionar con el juego Proton, pero depende del
      foco y del servidor gráfico.
    - Probar X11 y Wayland; si falla, usar `uinput` (requiere permisos) o
      `ydotool`.
    - Añadir opción de configuración para método de inyección.

## P2 — Producto y mantenimiento

11. **Localización es/en.**
    - Textos de acciones, Property Inspector y documentación.
    - Mantener español por defecto.

12. **Controles AKP05/AKP03 (hecho en 1.2).**
    - Soportado: acciones `cruiseenc`, `retarderenc` y `volumeenc` con
      `dialRotate`/`dialDown`, PI `encoder.html` y layout `$B1`.
    - Hecho: 34 acciones en el manifiesto Windows (27 Keypad + 11 nuevos + 3 Encoder);
      5 testigos eliminados y `fuel`/`range`/`route` rediseñados.

13. **Temas y accesibilidad.**
    - Tema claro/alto contraste para tiles.
    - Tamaño de texto configurable y pruebas de recorte a 72/112 px.

14. **Observabilidad.**
    - Rotación de `backend.log` por tamaño/fecha.
    - Nivel de log configurable (`-Verbose`).
    - Diagnóstico `-Test` que genere un contact sheet de las 34 acciones.

15. **Distribución y licencias.**
    - `scs-telemetry.dll` no se versiona; incluir checksum y aviso de licencia.
    - Revisar la licencia de los iconos `jp.tsuteto.ets2` antes de incluirlos en
      un release público.
    - Añadir `SHA256SUMS` por release.

16. **Auto-update y telemetría de errores.**
    - Comprobar versión en GitHub Releases.
    - No enviar datos personales; solo errores anónimos si el usuario acepta.

## Riesgos del port Linux

- **Bloqueante principal:** la memoria compartida de Windows bajo Proton.
  Sin puente, el backend Linux no ve la telemetría.
- **Wine:** requiere `wine` + `wine-mono`; el prefijo de Proton puede tener
  versiones distintas. Probar ATS y ETS2 por separado.
- **Wayland:** la inyección de teclas y el foco de ventana son más restrictivos
  que en X11.
- **Flatpak:** las rutas de configuración y el acceso a Wine cambian.
- **Rendimiento:** el puente Wine añade overhead; medir CPU frente al 0,97 % de
  Windows.

## Orden sugerido

1. Crear el port Linux con puente Wine y validar telemetría + teclas en ETS2.
2. Unificar manifiestos y versiones.
3. Completar paridad macOS y binario universal.
4. Extraer módulos y añadir pruebas.
5. Backend Linux nativo con socket.
