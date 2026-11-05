# Instalador macOS

El plugin principal (`com.hsgaming.scsmon.sdPlugin`) tiene backend nativo Swift
para macOS. El binario actual es **x86_64** y cubre el conjunto de acciones de
la **v1.0** de Windows. Es una **beta**: las acciones nuevas de la v1.1 todavÃ­a
no estÃ¡n portadas al backend Swift.

## Artefactos

En `installer/Output/`:

- `TruckerSim-Ajazz-macOS-1.0-beta.zip`: instalador de doble clic
  (`Instalar Trucker Simulator.command`). Se puede generar y compartir desde
  cualquier sistema.
- `TruckerSim-Ajazz-macOS-1.0-beta.pkg`: paquete nativo sin firma. Solo se
  genera ejecutando `build-macos.sh` en macOS.
- `SHA256SUMS-macOS.txt`: hashes de los artefactos.

El ZIP es la opciÃ³n recomendada para compartir mientras no haya certificado
Apple Developer. El `.pkg` evita usar Terminal, pero macOS puede pedir
**clic derecho > Abrir** por no estar notarizado.

## Contenido

```text
TruckerSim-Ajazz-macOS-1.0-beta/
  Instalar Trucker Simulator.command
  Desinstalar Trucker Simulator.command
  LEEME.txt
  LICENCIA-MIT.txt
  scs-sdk-plugin-MIT.txt
  payload/
    com.hsgaming.scsmon.sdPlugin/
      backend
      manifest.json
      icon*.png
      tile-*.png
      pk-*.png
      propertyinspector/
    libscs-telemetry.dylib
```

El instalador copia el plugin a:

```text
~/Library/Application Support/HotSpot/StreamDock/plugins/com.hsgaming.scsmon.sdPlugin/
```

Y la telemetrÃ­a a:

```text
American Truck Simulator.app/Contents/MacOS/plugins/libscs-telemetry.dylib
Euro Truck Simulator 2.app/Contents/MacOS/plugins/libscs-telemetry.dylib
```

## Compilar

En macOS, desde la raÃ­z del repositorio:

```bash
chmod +x installer/macos/build-macos.sh
./installer/macos/build-macos.sh
```

El script descarga la telemetrÃ­a macOS si no estÃ¡ en
`installer/macos/.cache/libscs-telemetry.dylib`, verifica su SHA256 y genera el
ZIP y el `.pkg`.

Para usar una copia local de la telemetrÃ­a:

```bash
TELEMETRY=/ruta/libscs-telemetry.dylib ./installer/macos/build-macos.sh
```

## TelemetrÃ­a macOS

Se usa el fork de `truckermudgeon` de `scs-sdk-plugin` v1.12.1, que publica
`/SCSTelemetry` en macOS (el mismo layout REV12 que Windows). Licencia MIT;
ver `docs/LICENCIAS.md`.

El `manifest-macos.json` de esta carpeta es una copia filtrada del manifiesto
principal: solo incluye las 21 acciones que el backend Swift implementa. No
editar a mano sin actualizar el backend y el manifiesto principal.

## Limitaciones conocidas

- Backend x86_64; en Apple Silicon requiere Rosetta 2.
- Sin firma ni notarizaciÃ³n: macOS puede bloquear el `.command` o el `.pkg`
  la primera vez.
- CGEvent necesita permiso de **Accesibilidad** para Stream Dock/backend.
- Las 11 acciones nuevas de la v1.1 no estÃ¡n incluidas:
  `range` (con motor), `engine`, `park`, `trailerattach`, `windowleft`,
  `windowright`, `checkengine`, `enginerange`, `enginegear`, `enginebtn` y
  `radiobtn`.
- La acciÃ³n `airpressure` del backend macOS todavÃ­a rotula `bar`, aunque el
  valor real estÃ¡ en PSI. Es una deuda del port, no del instalador.
- Los knobs del AKP05 y la tira tÃ¡ctil siguen fuera de alcance.

## Probar antes de compartir

1. Instalar el ZIP en un usuario limpio.
2. Abrir Stream Dock y comprobar que aparece la categorÃ­a.
3. Arrastrar velocidad, RPM y luces a teclas.
4. Abrir ATS/ETS2 y verificar que los valores cambian.
5. Probar un botÃ³n (por ejemplo luces) con el juego enfocado.
6. Desinstalar y comprobar que el plugin desaparece de Stream Dock.

La validaciÃ³n real requiere macOS y el juego. En Windows solo se puede
comprobar la estructura del paquete, los hashes y los scripts.
