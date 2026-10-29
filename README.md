# Trucker Simulator para Ajazz StreamDock

Plugin **Trucker Simulator** (`com.hsgaming.scsmon`): telemetria en vivo de
**American Truck Simulator** y **Euro Truck Simulator 2** en controladoras
Ajazz / StreamDock (AKP153R/E probado; AKP05, AKP03 y demas compatibles —
ver `docs/COMPATIBILIDAD.md`).

Velocidad, marchas, RPM, combustible, ruta, danos y todos los testigos del
tablero, mas botones que actuan sobre el juego (luces, intermitentes,
estacionarias, baliza, limpiaparabrisas, diferencial, freno, crucero).

![Teclas del tablero](docs/img/tiles-muestra.png)

## Caracteristicas

21 acciones en la v1.0 publicada (el arbol local ya define 32: ver
[Estado de desarrollo](#estado-de-desarrollo-2026-10-27)) en 2 grupos:

**Monitores** (solo muestran): velocidad (velocimetro), marcha, RPM con barra
de zona y aviso CAMBIA, combustible (surtidor + barra + autonomia), consumo
L/100km, crucero, limite de via con alerta, temperatura motor y aceite,
presion de aire, danos (camion / trailer / carga), ruta (faltante, tiempo
restante y avance).

**Botones** (muestran estado y actuan): estacionarias (`F`), intermitentes
izq/der (`[` / `]`), luces (`L`), largas (`K`), baliza (`O`),
limpiaparabrisas (`P`, ciclo OFF/A/2/3), bloqueo diferencial (`V`), freno de
estacionamiento (`Espacio`), crucero (`C`) y ventanillas izquierda/derecha
(toque = subir, mantener = bajar; sin asignar teclas a mano).

Detalles tecnicos: `docs/DESARROLLO.md`. Asignacion de teclas: `docs/INSTALACION.md`.

## Requisitos

- Windows 10/11 (64 bits).
- ATS y/o ETS2 (Steam o instalacion independiente).
- Software StreamDock / Ajazz con el dispositivo conectado.
- `scs-telemetry.dll` de RenCloud (v1.12.x) instalada en cada juego — el
  instalador lo hace solo; no se distribuye en este repo por licencia.

## Instalacion

1. Descarga `TruckerSim-Ajazz-Setup-X.Y.exe` de la seccion Releases.
2. Ejecuta el instalador: detecta ATS/ETS2 en Steam automaticamente. Si tu
   juego **no es de Steam**, usa el boton *Examinar* y apunta a su carpeta.
3. Abre (o reinicia) el software StreamDock: las acciones aparecen en la
   categoria **Trucker Simulator**.
4. En el juego, asigna `F` a estacionarias (Opciones → Teclas; viene sin
   asignar). El resto (`L K O [ ] P V C Espacio`) viene asignado por defecto.
5. **Ventanillas**: no hay que asignar nada. El plugin agrega solo los
   bindings que falten (`lwin*`/`rwin*`) al perfil activo y guarda un backup
   `controls.sii.hs-window-backup`. Si el juego estaba abierto durante la
   instalacion, cierralo y vuelve a abrirlo una vez: SCS solo lee
   `controls.sii` al arrancar.

Guia paso a paso con capturas: `docs/INSTALACION.md`.

## Dispositivos compatibles

| Dispositivo | Teclas | Estado |
|---|---|---|
| Ajazz AKP153R / AKP153E | 15 LCD (3×5) | Probado |
| Ajazz AKP05 / AKP05E | 10 LCD + 4 knobs + tira tactil | Compatible (teclas; knobs sin acciones asignadas) |
| Ajazz AKP03 y demas Keypad | LCD | Compatible (acciones Keypad estandar) |

Las acciones son `Keypad` estandar: funcionan en cualquier dispositivo sin
cambios. Detalle: `docs/COMPATIBILIDAD.md`.

## Diagnostico

```bat
backend.exe -Test        :: vuelca valores + PNGs a %TEMP%\scsmon-test
backend.exe -WatchHigh   :: muestrea largas 60 s (probar rafagas)
backend.exe -WindowsInfo :: muestra perfiles y bindings lwin*/rwin* detectados
backend.exe -WindowsFix  :: agrega los bindings que falten (juego cerrado)
```

Solucion de problemas comunes: `docs/INSTALACION.md#problemas-comunes`.

## Compilar desde fuente

- Backend: `csc.exe` de .NET Framework, sin dependencias (`docs/DESARROLLO.md`).
- Instalador: Inno Setup 6 (`installer/truckersim-ajazz.iss`).

## Estructura del repo

```text
plugins/com.hsgaming.scsmon.sdPlugin/  Plugin (fuentes: backend.cs, manifest, PI, PNGs)
automatizacion/                        Watcher gaming + lanzador manual
installer/                             Script Inno Setup
docs/                                  Manuales + imagenes
dll/                                   (vacío en git: colocar scs-telemetry.dll aqui para el instalador)
```

## Estado de desarrollo (2026-10-27)

> Bitacora interna para no perder contexto entre sesiones. Actualizar esta
> seccion al cerrar cada bloque de trabajo.

### Punto de reanudacion (2026-10-27)

- **P0, P1.1, P1.2, P2 y ventanillas automaticas completados, desplegados y
  validados en ETS2** (conduccion y dispositivo fisico, 27/10/2026).
- Binario instalado: SHA256 `2CEB60CADD276DE07DC0926C94E8FC1F841C869A70994975C1C39D21EBF610C8`
  (build con ventanillas mantenidas); backend PID `22084`, ~46-48 MB segun
  actividad. Los PID cambian al reiniciar StreamDock; el hash es el
  identificador estable.
- Ultima medicion (ETS2 pausado, 26 contextos en dos dispositivos):
  **1,16 s / 120 s = 0,97 %** de un nucleo; la primera muestra de 60 s dio
  1,07 %. Sin errores `render:`/`ev-err:`.
- Medicion conduciendo (ETS2, 180 s): **1,438 s / 180 s = 0,80 %** de un
  nucleo y 46,3 MB de working set medio (max 46,5 MB); velocidad 49-116,7 km/h
  y RPM 1.086-1.918. Sin errores `render:`/`ev-err:`. El muestreo inicial leyo
  `gear` como float y mostro 0; con `ReadInt32` (como el backend) marca 14,
  sin bug en el plugin.
- **Ventanillas (implementado)**:
  - Perfil activo de ETS2/ATS detectado desde el ultimo
    `New profile selected:` / `Set profile finished:` de cada
    `game.log.txt`; nombre -> carpeta hex
    (`gamercofla` -> `67616D6572636F666C61`).
  - Solo se agregan acciones **sin** tecla; nunca se pisa un binding del
    usuario. Backup `controls.sii.hs-window-backup` antes de escribir.
  - No se parchea con el juego abierto (SCS lee `controls.sii` al arrancar).
    Un watcher reintenta cada 15 s cuando el juego se cierra.
  - Perfil ETS2 actual: subir izq `F8`, bajar izq `Alt+F8`, subir der
    `Alt+coma`, bajar der `Alt+menos`. ATS conserva sus defaults
    (`slash`/`period`/`apostrophe`/`semicolon`).
  - Inyeccion **VK + scan** (ETS2 ignora `KEYEVENTF_SCANCODE` puro) y tecla
    mantenida 1,4 s para que la accion "mientras se mantiene" recorra la
    ventanilla. Toque = subir; mantener >400 ms = bajar.
  - Evidencia en `backend.log` (PID 22084): `ventanilla izq hold -> bajar
    (f8 +Alt)` seguido de `tecla scan 42 +Alt mantenida 1400ms ->
    foco:eurotrucks2`; lo mismo para derecha (`scan 33`/`0C`).
- **`checkengine` validado sin juego (2026-10-27)**: con un `Snap` sintetico y
  el render real, 4,0 % queda en negro; 5,0 % y 6,0 % muestran `CHECK
  ENGINE`; motor apagado, telemetria inactiva o desgaste nulo quedan
  apagados. Evidencia: `%TEMP%\opencode\checkengine-*.png` (1001 px no
  negros a 6 %). Confirma el umbral >= 5 % sin falsos positivos.
- **Estados sinteticos revisados (2026-10-27)**: ruta 350/451 y sin viaje,
  danos 9/2/1 y 100/60/21, crucero `OK`/`FUERA`/`OFF`, limite 80/90/sin
  limite, RPM 1500/1850 con `CAMBIA`, gauges normal/aviso y `checkengine`
  4/5/6. PNGs en `%TEMP%\opencode\syn-*.png`; sin recortes ni errores.
- **Tiles revisados con `-Test`** (ETS2 en vivo): `airpressure` = 132 PSI,
  `engine` con icono verde encendido, `park` rojo con freno echado,
  `damage` 9/2/1 %, `route` 350 km / 4:36 / 101-451 km, gauges de agua y
  aceite, RPM 500 en N y velocidad 0 km/h.
- **Validacion en vivo completada (2026-10-27)**: el usuario confirmo en el
  StreamDock fisico RPM/marcha, velocimetro y gauges, ruta/danos, limite 80 en
  alerta, crucero `OK`/`FUERA`, motor, PSI y freno de estacionamiento. Las
  ventanillas ya estaban confirmadas con los botones fisicos. No quedan
  pendientes de P0, P1.1-P1.2 ni P2.
- **Siguiente bloque**: decidir P3 (macOS/knobs/bindings configurables) o
  preparar el release; P1.3-P1.5 siguen en backlog y no urgen con **0,80 %**
  de CPU conduciendo.
- Compilar/desplegar: ver "Protocolo de trabajo". El binario no se commitea.
- Docs actualizados: `README.md`, `CHANGELOG.md`, `HISTORIAL.md` y
  `docs/INSTALACION.md` (ventanillas automaticas, sin asignacion manual).

### Situacion del arbol

- Ultimo commit / release: `363f498` (v1.0, 2026-10-14).
- **Sin commit**: `backend.cs`, `manifest.json`, `README.md`, `CHANGELOG.md`,
  `HISTORIAL.md` y `docs/INSTALACION.md`. El manifiesto local define
  **32 acciones**; la v1.0 publicada tiene 21.
- **Sin seguimiento**: `backend.swift`, binario macOS `backend`, PNGs nuevos
  `pk-*`, `fps/` y `plugins/com.hsgaming.vitals.sdPlugin/`.
- `backend.exe` del repo y el instalado en
  `%APPDATA%\HotSpot\StreamDock\plugins\com.hsgaming.scsmon.sdPlugin\` son
  identicos (SHA256 `2CEB60...`, build con ventanillas mantenidas).
- Documentacion: la v1.0 publicada documenta 21 acciones; el arbol local
  define 32 y `CHANGELOG.md` tiene una seccion `[Sin publicar]` con P0, P1 y
  P2.

### Acciones locales nuevas (11)

`range`, `engine`, `park`, `trailerattach`, `windowleft`, `windowright`,
`checkengine`, `enginerange`, `enginegear`, `enginebtn`, `radiobtn`.
No todas estan probadas en juego ni documentadas en los manuales.

### Medicion de referencia

Sesion del 27/10/2026, PID `14148`, desde las 16:53 hasta las 20:51:

- CPU acumulada: ~1229 s (~8,5 % de un nucleo durante toda la sesion).
- Working set: ~39 MB.
- Con 15 teclas visibles a 0,15 s son ~100 renders/s. Cada render crea
  `Bitmap`, `Graphics`, `Font`, `Brush` y `StringFormat`, y codifica PNG +
  base64.
- `FlashWatch` mantiene un hilo propio leyendo el MMF cada 15 ms (~66
  lecturas/s) durante toda la sesion.
- Tras P0 (21:09): binario nuevo desplegado, sin errores de render en el log.
  Medicion preliminar con el build P0 (PID `21308`): 5,06 s de CPU en 1,1 min
  (~7,7 % de un nucleo) y 39,7 MB de working set; sirve de base para P1.
  Tras el redespliegue final, backend activo PID `10188`.
- **P1.1 (render bajo demanda)**, misma partida pausada y mismo equipo:
  - P0 (PID `10188`): **4,63 s de CPU en 60 s = 7,71 %** de un nucleo, WS
    41,1 MB.
  - P1.1 (PID `16936`): **1,31 s de CPU en 60 s = 2,19 %** de un nucleo, WS
    39,4 MB.
  - Reduccion: **~72 % de CPU** en reposo; sin errores `render:` ni `ev-err:`
    en el log. El ahorro viene de saltar render + PNG + base64 de los tiles
    cuyo estado visible no cambio.
- **P1.2 (iconos preescalados + base64 cacheado)**, misma partida pausada y
  18 teclas visibles, con animaciones activas (baliza, hazard, intermitentes,
  trailer acoplado y freno de estacionamiento):
  - Primer build (PID `7764`): 0,72 s de CPU en 60 s = **1,20 %**, WS 41,5 MB.
  - Build final, con la metrica incluida en la clave (PID `6028`): 0,64 s de
    CPU en 60 s = **1,07 %**, WS 40,5 MB.
  - Acumulado P0 -> P1.2: **7,71 % -> 1,07 % (~86 % menos CPU)**. Sin errores
    `render:` ni `ev-err:` en el log.
  - La medicion de P1.1 (2,19 %) tenia `parkBrake=0`; la de P1.2 tiene
    `parkBrake=1`, es decir, al menos un tile animado mas, y aun asi baja a
    la mitad.
- **P2 (visualizacion)**, misma partida pausada y 26 contextos activos en dos
  dispositivos:
  - Primera muestra de 60 s (PID `21144`): 0,64 s = **1,07 %**, WS 40,8 MB.
  - Muestra larga de 120 s: 1,16 s = **0,97 %**, WS 40,9 MB.
  - Sin regresion frente a P1.2: el dibujo nuevo (barras, anillos, pildoras y
    pulsos unificados) solo se ejecuta cuando cambia el estado visible.

### Hallazgos

> Estado: los puntos P0.1-P0.7 y P1.1-P1.2 ya estan corregidos (ver
> historial). Se dejan aqui como referencia de lo detectado en la auditoria.

**Correctitud / bajo riesgo**

1. `airpressure` rotula `"bar"`, pero los valores observados (~119-145) y los
   umbrales `airWarn`/`airEmergency` parecen **PSI**. Confirmar en juego y
   rotular PSI (o convertir a bar).
2. `engine` usa `RenderPack("low", s.engine)`: pinta el icono de **luces
   cortas** en vez del estado del motor. Usar icono/texto propio.
3. `checkengine` se activa por `fuelWarn`, temperatura y aire, pero no por
   desgaste del motor. La app Windows usa desgaste >= 5 %; unificar criterio.
4. `emergencybrake` envia `Espacio` y usa icono de freno de mano: es **freno
   de estacionamiento**, no de emergencia. Renombrar accion y docs.
5. Ventanillas con VK `0xBA/0xDB/0xBE/0xBD`: dependen del layout de teclado.
   Migrar a scan codes o a bindings configurables.
6. `willDisappear` solo marca `visible=false`; el contexto y
   `pressDownTimes` quedan en memoria. Limpiar al desaparecer.
7. Varios `StringFormat` no se liberan: fuga de handles GDI en sesiones largas.
8. Confirmar semantica de `engine` (O_ENG) frente a `motor` (O_MOTOR): hoy se
   usan para cosas distintas.

**Rendimiento**

> Estado: los puntos 2-3 estan resueltos por P1.1/P1.2; el 1 se descarto por
> medicion (el coste es el rasterizado de texto, no crear `Font`/`Brush`);
> 4-6 quedan en backlog por impacto marginal con 0,97 % de CPU en reposo.

1. Cachear `Font`, `Brush` y `StringFormat` (por tema) y reutilizar el
   `Bitmap` de 72x72.
2. Render bajo demanda: clave de estado por accion; saltar el render si no
   cambio (excepto animaciones de pulso).
3. Evitar PNG/base64 cuando la imagen no cambio. Ya se compara el string final,
   pero se paga render + codificacion igualmente.
4. `Ws.SendRaw` crea `RandomNumberGenerator`, `MemoryStream` y dos buffers por
   mensaje. Reutilizar RNG y buffers (o mascarar sobre el buffer de salida).
5. `FlashWatch`: abrir el MMF una sola vez y bajar el sondeo con el juego
   pausado.
6. Quitar/limitar `Log("ev rx")` y `Log("ev ...")` por evento: I/O en cada
   mensaje.
7. Medir antes/despues con `Get-Process backend | Select CPU,WorkingSet64`.

**Visualizacion**

> Estado: los puntos 1-5 estan resueltos por P2; 6-7 quedan como ideas
> (supersampling y tema con contraste configurable).

1. RPM: arco o barra hacia el corte, no solo numero + CAMBIA.
2. Gauges: anillo/barra dinamica y zona de warning; hoy el anillo es estatico.
3. Ruta y danos: agrandar valores principales y reducir ruido.
4. Crucero: icono demasiado transparente; subir contraste y mostrar velocidad
   fijada.
5. Alertas: pulso y color consistentes por severidad.
6. Evaluar supersampling 2x (144 px -> 72 px) para bordes mas suaves; medir
   costo.
7. Tema con contraste configurable.

### Backlog priorizado

- **P0 - correctitud (bajo riesgo)**: COMPLETADO 2026-10-27 (ver historial).
- **P1 - rendimiento**: P1.1 (render bajo demanda) y P1.2 (iconos
  preescalados + tiles cacheados) COMPLETADOS 2026-10-27. P1.3 (`SendRaw`),
  P1.4 (`FlashWatch`) y P1.5 (logging) quedan en backlog: con 0,97 % de CPU
  en reposo y 0,80 % conduciendo su impacto esperado es marginal.
- **P2 - visualizacion**: COMPLETADO 2026-10-27 (RPM, gauges, ruta/danos,
  crucero y alertas). Desplegado, medido y validado en vivo (conduccion y
  dispositivo fisico); **0,97 %** de un nucleo en reposo y **0,80 %**
  conduciendo.
- **P3 - alcance/paridad**: `backend.swift` (macOS), knobs AKP05, bindings
  configurables de teclado.

### Decision (2026-10-27)

Se implementa **P0 primero**, en este orden, actualizando esta bitacora al
terminar cada punto:

1. [x] Etiqueta de presion de aire (bar -> PSI, confirmado en vivo).
2. [x] Icono/estado de la accion `engine`.
3. [x] Criterio de `checkengine` (desgaste del motor >= 5 %).
4. [x] Renombrar `emergencybrake` a freno de estacionamiento.
5. [x] Ventanillas por VK+scan y bindings automaticos (sin asignacion manual).
6. [x] Liberar `StringFormat` en los renders (instancia compartida).
7. [x] Limpiar contextos en `willDisappear`.

**P1 (rendimiento) iniciado el 2026-10-27**, en este orden:

1. [x] Render bajo demanda por clave de estado; saltar PNG/base64 si no cambio.
   Medido: 7,71 % -> 2,19 % de un nucleo en reposo.
2. [x] Cache de recursos de dibujo: preescalado de iconos a 72x72 y tiles de
   icono precomputados en base64. La cache de `Font`/`Brush` se descarta por
   medicion (el coste dominante es el rasterizado de texto). Medido:
   2,19 % -> 1,07 % de un nucleo.
3. [ ] Menos allocaciones en `Ws.SendRaw` (backlog, impacto marginal en reposo).
4. [ ] `FlashWatch` con MMF persistente y sondeo adaptativo (backlog).
5. [ ] Reducir logging por evento (backlog).
6. [x] Medir CPU/working set antes y despues de cada punto.

**P1.2 (decidido 2026-10-27)**: el banco sintetico da `RenderKey` medio
~453 us, de los cuales ~139 us son PNG+base64; un icono 144->72 + B64 cuesta
~753 us y el mismo icono preescalado a 72 ~378 us; devolver un string
precomputado cuesta ~0 us. Se implementa:

- Preescalar los `pk-*.png` a 72x72 al cargar (`InterpolationMode.Default`,
  identico al render actual: delta maximo 1).
- Precomputar el base64 de los tiles de icono (`RenderPack`), que pasan a ser
  gratis.
- Mantener los originales 144x144 solo para los dos renders que dibujan a
  44x44 (`wiper`) y 32x48 (`hazbtn`), preservando su aspecto exacto.

Se descarta por ahora cachear `Font`/`Brush`: el coste dominante es el
rasterizado de texto, no la creacion del recurso.

Resultado P1.2: 2,19 % -> **1,07 %** de un nucleo; `RenderPack` pasa de
~658 us a ~0,2 us y los pulsos con icono preescalado de ~739 us a ~345 us.
Se agrego el nombre de la metrica a `StateKey` para que dos acciones con
valores coincidentes nunca compartan una clave.

**P2 (visualizacion) iniciado el 2026-10-27**, en este orden:

1. [x] RPM con barra inferior de 60x4 px (no arco): un numero de 4 cifras a
   18 pt mide 63 px y un anillo de 270 grados obliga a bajarlo a menos de
   15 pt o a recortarlo. La barra conserva el numero grande, muestra el
   avance con el color de zona y marca el ultimo 10 % en rojo. "CAMBIA" pasa
   de 13 a 11 pt porque a 13 pt medía 77 px y se salía del tile. `StateKey`
   incluye `rpmMax` para invalidar la barra al cambiar de motor.
2. [x] Gauges dinamicos: anillo de 270 grados con progreso, zona de peligro y
   marca de umbral. Agua 40-120 C (umbral `waterWarn`), aceite 40-150 C
   (umbral 130) y aire 0-150 PSI (peligro al principio, umbral `airWarn`).
   Se mantienen las condiciones de aviso y el color rojo; la clave incluye
   `waterWarn`/`airWarn` porque mueven la marca.
3. [x] Ruta/danos: km restantes a 19 pt, tiempo restante compacto
   (`RESTAN 45m`/`1:30`), barra de avance y "recorrido / plan" a 7 pt. En
   danos, iconos atenuados, valores 15 pt (17 pt no cabe: "100%" mide 67 px) y
   barra vertical proporcional de 3 px; el 100 % usa 12 pt para no recortarse.
4. [x] Crucero: icono a 0,32 de opacidad en el gauge y 0,35 en el boton,
   fondo mas oscuro (alfa 180/130) y estado en pildora de 64x16 a 11 pt:
   verde `OK`, ambar `FUERA`, rojo `OFF`. Se mantiene la velocidad fijada y
   la tolerancia de +/-5 km/h.
5. [x] Alertas: helper `PulseAlpha` (coseno de 1 s, alfa 0,35-1,0) y paleta
   `PulseRed`/`PulseAmber`/`PulseGreen`/`PulseCyan`. Reemplaza las seis
   animaciones duplicadas de baliza, bloqueo, trailer, freno, radio y check
   engine; el exceso de limite usa `UrgentBlink` a 2 Hz, sincronizado con
   `Tick250` para que la clave y el dibujo coincidan.
6. [x] Verificar con `-Test`, comparar PNGs antes/despues y medir CPU con el
   mismo perfil: **1,16 s / 120 s = 0,97 %** de un nucleo y 40,9 MB, sin
   errores en el log (PID `21144`, SHA256 `CD611A4F...`).

Criterios: no cambiar la semantica ni los datos; mantener Arial y 72x72; el
coste de dibujo debe seguir siendo marginal frente a P1.2 (1,07 % en reposo).
La clave de estado se amplia solo si el nuevo dibujo depende de un dato que
antes no estaba (por ejemplo `rpmMax`).

Resultado P2.1: barra de RPM de 60x4 px con fondo, progreso por zona y ultimo
10 % en rojo. Previews sinteticos a 0/1000/1500/1800/2000 RPM sin recortes;
`-Test` OK.

Resultado P2.2: anillo de 270 grados con fondo, zona de peligro y marca de
umbral; agua 62/110, aceite 89/140, aire 143/60/30 y valor nulo verificados en
previews. `-Test` OK.

Resultado P2.3: ruta 123/168 km y 1000/2500 km sin recortes, con tiempo
restante (`RESTAN 1:30`/`15:00`) y barra de avance; danos 9/2/1, 100/50/0 y
nulos verificados. La barra de severidad proporcional deja un punto en danos
bajos y crece con el dano. `-Test` OK.

Resultado P2.4: crucero con icono mas visible y fondo mas oscuro; estados
`OK` (verde), `FUERA` (ambar) y `OFF` (rojo) verificados en previews. El boton
mantiene la velocidad fijada grande y el testigo verde de activacion.

Resultado P2.5: `PulseAlpha` reemplaza las animaciones duplicadas y la paleta
por severidad queda rojo/ambar/verde/cian. Previews de check engine, limite,
bloqueo, trailer, freno, radio, baliza y boton de crucero; check engine y
limite cambian 1001 y 696 pixeles entre fases, confirmando el pulso. `-Test`
OK.

Despliegue P2.1-P2.5: SHA256 `CD611A4F6A4C29DC9515F1E65574AA6C7DFEBCECB2CB8BC8A9682481CFA89F35`,
PID `21144`, 26 contextos activos en dos dispositivos. Medido con el juego
pausado: **1,16 s / 120 s = 0,97 %** de un nucleo y 40,9 MB; la primera muestra
de 60 s dio 1,07 %. Log sin errores.

### Protocolo de trabajo

```bat
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /optimize /target:exe ^
  /out:plugins\com.hsgaming.scsmon.sdPlugin\backend.exe ^
  plugins\com.hsgaming.scsmon.sdPlugin\backend.cs /reference:System.Drawing.dll
```

Despliegue manual (cerrar antes StreamDock y el backend):

```bat
set INST=%APPDATA%\HotSpot\StreamDock\plugins\com.hsgaming.scsmon.sdPlugin
copy /Y plugins\com.hsgaming.scsmon.sdPlugin\backend.exe "%INST%\backend.exe"
copy /Y plugins\com.hsgaming.scsmon.sdPlugin\manifest.json "%INST%\manifest.json"
```

1. Compilar y probar sin juego con `backend.exe -Test`.
2. Copiar `backend.exe` y `manifest.json` a la carpeta instalada, cerrar el
   backend en curso y reiniciar StreamDock.
3. Probar en ATS y ETS2.
4. Al cerrar cada bloque: actualizar esta seccion, `CHANGELOG.md` y el README
   de acciones.

### Historial de la bitacora

- **2026-10-27**: auditoria inicial. Sin cambios de codigo. Medida la sesion de
  referencia y definido el backlog P0-P3.
- **2026-10-27 - P0.1 (hecho)**: presion de aire confirmada en vivo con ETS2:
  `airPressure=133.1`, `airWarn=69.6`, `airEmergency=34.8`. Son **PSI**. La
  accion `airpressure` ahora rotula `PSI` en vez de `bar`; no se convierte el
  valor.
- **2026-10-27 - P0.2 (hecho)**: corregida la semantica motor/freno motor.
  `O_MOTOR` (1567) es **motorBrake**, no el motor encendido; el estado real es
  `O_ENG` (1576). La accion `engine` usa ahora el icono `pk-engine`/`pk-engine-c`
  (`RenderEngineState`) en vez de `pk-low`; `enginebtn`, `enginerange` y
  `enginegear` usan `s.engine`. En `lights` la fila `MT` pasa a `MB` (motor
  brake). El `-Test` incluye ya todas las acciones nuevas.
- **2026-10-27 - P0.3 (hecho)**: `checkengine` alineado con la app Windows:
  `activo && motor encendido && desgaste motor >= 5 %`. Se agrego
  `engineWear` (O_WENG=1036) y se quitaron los disparadores de combustible,
  temperatura y aire, que no corresponden a un testigo de motor. En la prueba
  en vivo el desgaste era 4,41 % (apagado correcto). El umbral se valido luego
  con `Snap` sintetico: 4,0 % negro; 5,0 % y 6,0 % encienden; motor apagado,
  `active=false` o desgaste nulo quedan apagados.
- **2026-10-27 - P0.4 (hecho)**: `emergencybrake` pasa a "Freno de
  estacionamiento (boton)" (UUID intacto) y usa `pk-parking-brake-1`. La accion
  `park` pasa a "Freno de estacionamiento (testigo)" y ahora lee `s.park`
  (freno real) en vez de `s.parking` (luces de posicion). Funcion renombrada a
  `RenderParkingBrake`.
- **2026-10-27 - P0.5 (hecho)**: ventanillas por scan codes (`27/1A` izquierda,
  `34/0C` derecha) para no depender del layout. En el perfil de ETS2 las
  ventanillas estaban sin asignar; luego se reemplazo por bindings
  automaticos VK+scan (ver seccion de ventanillas).
- **2026-10-27 - P0.6 (hecho)**: un solo `StringFormat` compartido
  (`CenterSF`) para todos los renders. Elimina la creacion por frame y la fuga
  de handles GDI+ de las 12 copias anteriores.
- **2026-10-27 - P0.7 (hecho)**: `willDisappear` ahora elimina el contexto y
  `pressDownTimes` en vez de solo marcarlo invisible. Evita crecimiento
  indefinido al cambiar de perfil/pagina.
- **2026-10-27 - P0 verificado y desplegado**: compila con `csc /optimize`,
  `-Test` renderiza las 32 acciones sin errores, umbral de `checkengine`
  probado con 4,9 % (negro) y 5,1 % (enciende), y luego con 4,0 % (negro),
  5,0 %/6,0 % (enciende), motor apagado e inactivo (negro). `backend.exe` y
  `manifest.json` copiados a la carpeta instalada (SHA256
  `6007F5...`); StreamDock reiniciado y backend nuevo activo (PID `10188`).
  Nota: el perfil de ETS2 tiene las ventanillas sin asignar; el usuario debe
  asignar `;` `[` `.` `-` (posicion fisica US) para probarlas.
- **2026-10-27 - P1.1 (hecho)**: render bajo demanda en `backend.cs`.
  `Ctx.lastKey` guarda una clave de estado por contexto; si no cambio, se
  reutiliza la ultima imagen y se omite `RenderKey` + PNG + base64. La clave
  usa los valores ya redondeados que se dibujan (`R0/R1/Kmh`), los colores de
  umbral (`DmgCol`, zona RPM, avisos) y buckets de tiempo (`Tick100`/`Tick250`)
  solo para animaciones activas. Tambien se corrigio la clave de `lights`
  (usa `highB`, no el latch de `HighShown`) y se incluye `motorBrake`.
  Compilado y `-Test` OK; desplegado (SHA256 `19B89562...`) y medido con el
  juego pausado: **1,31 s / 60 s = 2,19 %** de un nucleo y 39,4 MB, frente a
  4,63 s / 7,71 % y 41,1 MB de P0. Sin errores en el log (PID `16936`).
- **2026-10-27 - P1.2 (hecho)**: rendimiento de iconos. `Preload` carga los
  PNG originales, genera una copia 72x72 (`InterpolationMode.Default`) y
  precomputa el base64 de cada tile (`iconB64`); `RenderPack` devuelve el
  string cacheado y `hazbtn` tiene sus dos estados precomputados. Los renders
  que dibujan a 44x44/32x48 siguen usando los originales (`imgsBig`) para
  conservar el aspecto exacto. Verificacion: `hazbtn`, intermitentes,
  limpiaparabrisas y `lowB` identicos (delta 0); iconos a 72 con delta <=1.
  Banco sintetico: `RenderPack` 658 -> 0,2 us; pulso con icono preescalado
  739 -> 345 us; `RenderKey` medio 453 -> 309 us. Tambien se agrego el nombre
  de la metrica a `StateKey` para evitar colisiones entre acciones con valores
  coincidentes. Desplegado (SHA256 `0569578C...`) y medido con 18 teclas y
  animaciones activas (baliza, hazard, intermitentes, trailer y freno de
  estacionamiento): **0,64 s / 60 s = 1,07 %** y 40,5 MB (primer build sin la
  metrica en la clave: 1,20 %). Sin errores en el log (PID `6028`).
- **2026-10-27 - P2.1 (hecho)**: RPM con barra inferior en vez de arco. Se
  midio el texto: "2000" a Arial Bold 18 pt ocupa 63 px, mientras que un anillo
  de 270 grados a r=31 solo deja 43-53 px de hueco segun la altura; el numero
  tendria que bajar a menos de 15 pt o recortarse. La barra de 60x4 conserva
  el 18 pt, muestra el avance con el color de zona y marca el ultimo 10 % en
  rojo. "CAMBIA" pasa a 11 pt (antes 13 pt = 77 px, se salia del tile).
  `StateKey` incluye `rpmMax` para que un cambio de motor invalide la barra.
  Previews 0/1000/1500/1800/2000 RPM sin recortes; `-Test` OK (en ese momento
  sin desplegar).
- **2026-10-27 - P2.2 (hecho)**: gauges con anillo de 270 grados. El progreso
  usa el rango de cada dato (agua 40-120 C, aceite 40-150 C, aire 0-150 PSI),
  la zona de peligro se dibuja al final en temperatura y al principio en aire,
  y una marca blanca senala el umbral. Se conservan las condiciones de aviso:
  agua >= `waterWarn`, aceite >= 130, aire <= `airWarn`/`airEmergency`. La
  clave incluye `waterWarn`/`airWarn` porque mueven la marca. Previews
  normal/aviso/emergencia/nulo y `-Test` OK.
- **2026-10-27 - P2.3 (hecho)**: ruta y danos. Ruta: "FALTAN km" 7 pt, km
  restantes 19 pt, tiempo restante compacto (`45m`/`1:30`) 7 pt, barra de
  avance 60x4 y "recorrido / plan" 7 pt; la clave usa `FmtETA` porque `routeT`
  es tiempo de juego y sumarlo al reloj real daria una llegada incorrecta.
  Danos: iconos atenuados, valores 15 pt, barra vertical de severidad de 3 px
  proporcional al dano y 12 pt solo para 100 % (a 15 pt mide 59 px y a 17 pt
  67 px, no cabe). Previews 9/2/1, 100/50/0, nulos, ruta 123/168 y 1000/2500;
  `-Test` OK.
- **2026-10-27 - P2.4 (hecho)**: crucero. El icono pasa de 0,10/0,20 a
  0,32/0,35 de opacidad, el fondo de 145/90 a 180/130 y el estado se dibuja en
  una pildora de 64x16 a 11 pt: `OK` verde, `FUERA` ambar y `OFF` rojo. Se
  mantiene la velocidad fijada y la tolerancia de +/-5 km/h. Previews de
  OK/FUERA/OFF/sin velocidad y `-Test` OK.
- **2026-10-27 - P2.5 (hecho)**: pulso y paleta unicos. `PulseAlpha` usa un
  coseno de 1 s entre 0,35 y 1,0; `PulseRed` (255,60,45), `PulseAmber`
  (255,160,30), `PulseGreen` (0,235,110) y `PulseCyan` (0,220,255). Se
  reemplazaron las animaciones duplicadas de baliza, bloqueo, trailer, freno,
  radio y check engine. El exceso de limite usa `UrgentBlink` (2 Hz) con
  `Tick250`, igual que la clave, en vez de `DateTime.UtcNow`. Check engine y
  limite cambian 1001/696 pixeles entre fases; contact sheet de las ocho
  alertas OK. `-Test` OK. Pendiente en ese momento: desplegar P2.1-P2.5 y medir.
- **2026-10-27 - P2.1-P2.5 desplegado (hecho)**: `backend.exe` y
  `manifest.json` copiados a la carpeta instalada (SHA256 `CD611A4F...`);
  StreamDock reiniciado y backend nuevo activo (PID `21144`). Medido con el
  juego pausado: **1,16 s / 120 s = 0,97 %** de un nucleo y 40,9 MB de working
  set, con 26 contextos activos en dos dispositivos. La primera muestra de
  60 s dio 1,07 %, dentro del ruido de P1.2. Log sin errores `render:`/`ev-err:`.
  El perfil actual conserva baliza, intermitentes, hazard, trailer, freno de
  estacionamiento y motor encendido.
- **2026-10-27 - validacion sintetica final (hecho)**: sobre el build
  `2CEB60...` se renderizaron estados con `Snap` sintetico: `checkengine`
  4/5/6 %, ruta 350/451 y sin viaje, danos 9/2/1 y 100/60/21, crucero
  `OK`/`FUERA`/`OFF`, limite 80/90/sin limite, RPM 1500/1850 con `CAMBIA` y
  gauges normal/aviso. Todos los tiles son legibles y sin recortes; no hubo
  cambios de codigo. Evidencia en `%TEMP%\opencode\syn-*.png`.
- **2026-10-27 - validacion en vivo (hecho)**: conduciendo ETS2 durante 180 s
  se midio **0,80 %** de un nucleo y 46,3 MB. El usuario confirmo en el
  StreamDock fisico: RPM/marcha, velocimetro/gauges, ruta/danos, limite 80 en
  alerta, crucero `OK`/`FUERA`, motor, PSI y freno de estacionamiento. Sin
  errores en `backend.log`. No quedan pendientes de P0, P1.1-P1.2 ni P2.

## Creditos

- Telemetria: `scs-telemetry.dll` de RenCloud (licencia propia).
- Iconos de testigos: pack `jp.tsuteto.ets2` (solo respaldo local, no incluido).
- Icono del paquete: logo ATS de Steam.

## Licencia

Codigo propio (backend.cs, scripts, instalador, docs): MIT. Los binarios de
terceros quedan excluidos (ver arriba).
