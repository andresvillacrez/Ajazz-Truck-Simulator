# Instalacion

## Instalador (recomendado)

1. Ejecuta `TruckerSim-Ajazz-Setup-X.Y.exe`.
2. El instalador busca ATS y ETS2 en tus bibliotecas de Steam (registro +
   `libraryfolders.vdf`, detecta varias bibliotecas).
3. Si un juego **no es de Steam** (o esta en ruta rara), pulsa *Examinar* y
   selecciona su carpeta (la que contiene `bin/` o el ejecutable).
4. El instalador copia:
   - `scs-telemetry.dll` → `<juego>\bin\win_x64\plugins\` (crea la carpeta).
   - El plugin → `%APPDATA%\HotSpot\StreamDock\plugins\com.hsgaming.scsmon.sdPlugin\`.
5. Cierra el software StreamDock si esta abierto (el instalador lo intenta
   cerrar solo) y vuelve a abrirlo: las acciones aparecen en la categoria
   **Trucker Simulator**.

## Configuracion en el juego

En Opciones → Teclas → Camion:

| Accion | Tecla | Nota |
|---|---|---|
| Estacionarias (hazard) | `F` | **Asignarla manual**: viene sin asignar |
| Intermitente izq / der | `[` / `]` | Por defecto |
| Luces | `L` | Por defecto |
| Largas | `K` | Por defecto |
| Baliza | `O` | Por defecto |
| Limpiaparabrisas | `P` | Por defecto |
| Bloqueo diferencial | `V` | Por defecto |
| Freno estacionamiento | `Espacio` | Por defecto |
| Crucero | `C` | Por defecto |

> El juego debe estar **enfocado** para que los botones envien teclas. El
> layout espanol rompe los codigos virtuales de `[`/`]` y por eso el plugin
> los envia por scan code: no los reasignes a otras teclas.

## Instalacion manual (sin instalador)

1. Copia `scs-telemetry.dll` (RenCloud v1.12.x) a
   `<juego>\bin\win_x64\plugins\` de ATS y/o ETS2.
2. Copia la carpeta `com.hsgaming.scsmon.sdPlugin` (con `backend.exe`
   compilado) a `%APPDATA%\HotSpot\StreamDock\plugins\`.
3. Reinicia el software StreamDock.

## Perfiles fuera de OneDrive (recomendado si usas OneDrive)

Los perfiles guardan constantemente y OneDrive los persigue. Mueve los
perfiles fuera y deja un junction:

```bat
mklink /J "%USERPROFILE%\OneDrive\Documentos\Euro Truck Simulator 2" "C:\Users\%USERNAME%\Documents\Euro Truck Simulator 2"
mklink /J "%USERPROFILE%\OneDrive\Documentos\American Truck Simulator" "C:\Users\%USERNAME%\Documents\American Truck Simulator"
```

## Problemas comunes

- **Testigos congelados / sin cambios**: el juego esta en **pausa** — la
  telemetria se congela en pausa. Pulsa ESC → Conducir.
- **Rafagas no encienden el testigo**: haz la prueba con
  `backend.exe -WatchHigh` (ver README). Si `HI` nunca sube, revisa la tecla
  de rafaga en el juego.
- **Pantalla no refresca**: conecta el Ajazz directo al PC (sin hub), con el
  software cerrado, y vuelve a abrir.
- **Botones no hacen nada**: el juego debe estar enfocado; revisa la tabla de
  teclas de arriba (sobre todo `F`).
- **Plugin sin datos al arrancar**: el backend reconecta solo cuando abres el
  juego (espera ~1 s). Sin juego muestra `-- / sin juego`.
