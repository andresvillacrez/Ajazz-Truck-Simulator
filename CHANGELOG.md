# CHANGELOG

## [1.3.2] - 2026-11-05

### Correccion
- Texto "Trucker Simulator" en tiles sin juego ahora se muestra en dos lineas (Trucker / Simulator) para evitar desbordamiento.


## [1.3.1] - 2026-11-05

### Presentacion
- Logo del proyecto como encabezado del repositorio (README).
- Capturas de la app Ajazz StreamDock con el plugin en accion (`docs/img/`).

## [1.3.0] - 2026-11-04

### Acciones
- Eliminadas 5 acciones redundantes/testigos: `engine`, `checkengine`,
  `enginerange`, `enginegear` y `park`.
- Rediseñados `fuel`, `range` y `route` con nuevas layouts, barras y colores
  semánticos.
- Nuevo diseño de combustible: porcentaje grande, litros, autonomía y barra
  de nivel con aviso ámbar/rojo.
- Nueva vista de autonomía con número grande, road motif y color por distancia.
- Ruta mejorada: distancia restante, ETA, barra de avance con marcador y
  recorrido/plan.

### Ports
- Nueva estructura `ports/windows`, `ports/macos` y `ports/linux-opendeck`.
- Fuentes movidas a `ports/windows/src/backend.cs` y
  `ports/macos/src/backend.swift`; los recursos compartidos permanecen en
  `plugins/com.hsgaming.scsmon.sdPlugin/`.
- Añadido port Linux/OpenDeck experimental con puente Wine/Proton
  (`backend-linux.sh`), manifiesto Linux, build e instalador.
- Añadida auditoría priorizada en `ports/MEJORAS.md`.

### Backend Windows
- Muestreo y render desacoplados: hilo receptor WebSocket dedicado, cola de
  eventos y bucle de muestreo a 30 Hz.
- Render bajo demanda con límite de 33 ms por contexto; el intervalo del
  Property Inspector se conserva como máximo (mínimo 0.02 s).
- `Ws.SendRaw` protegido con lock para evitar tramas mezcladas entre el hilo
  receptor y el de render.
- `Ws.SendRaw` sin `MemoryStream` ni copia completa del frame: cabecera y
  mascara en buffers reutilizados, payload enmascarado por bloques de 8 KB y
  un solo RNG. Validado con tramas de 5 B, 1 KB y 100 KB.
- Benchmark local (500 tramas de 30 KB, loopback): 132 ms -> 102 ms.
- P1.4: `FlashWatch` reutiliza el visor de telemetria del bucle principal y
  adapta el sondeo: 15 ms activo, 100 ms en pausa, 250 ms sin juego/MMF.
  Mantiene el latch de 1,5 s para el destello de largas y registra el cambio
  de modo en `backend.log`.
- Log de eventos por frame eliminado salvo `-Verbose`; menos I/O en el camino
  caliente.
- Property Inspector: intervalo por defecto 0.05 s, mínimo 0.02 s.
- Cola de entrada dedicada (`scsmon-input`): `PressKey`, `PressScan` y
  `PressScanHold` ya no bloquean el hilo de render/telemetría.
- Render adaptativo por dispositivo: 96 px para AKP153/AKP05 y 144 px para
  AKP03E, detectado desde `deviceDidConnect` (`deviceInfo.size`/nombre).
- Iconos y tiles cacheados por tamaño; supersampling con `ScaleTransform` y
  `AntiAliasGridFit` para texto más nítido.
- Nuevo diagnóstico `-RenderDiag`: genera 22 acciones a 72/96/144 px en
  `%TEMP%\scsmon-render-*`.

### Encoders AKP03E / AKP05
- Nuevas acciones `cruiseenc`, `retarderenc` y `volumeenc` para los mandos
  giratorios, con `Controllers: ["Knob"]` y layout `$B1`.
- Nuevas acciones para controles no presentes: `liftaxle`, `liftaxlet`,
  `cruiseinc`, `cruisedec`, `retarderauto`, `frontsuspup`,
  `frontsuspdwn`, `rearsuspup`, `rearsuspdwn` y `suspreset`.
- Nuevas acciones de camara: `camexterior`, `caminterior` y `camroof`,
  con iconos `cam/blue` y teclas 1/2/4 del juego.
- Nuevo plugin separado `com.hsgaming.scsmon.testigos.sdPlugin` con 12
  testigos: velocidad, marcha, RPM, combustible, consumo, limite,
  temperatura motor/aceite, presion, danos, ruta y autonomia.
- Esas 12 acciones se retiraron del plugin principal para evitar duplicados.
- Camara: blanco inactivo y azul con pulse cuando esta seleccionada.
- Nuevas acciones de marcha: `geardrive`, `gear0` y `gearreverse`, con
  iconos `gears` y estado activo segun la marcha registrada. Usan
  auto-binding por `Alt+tecla` si el perfil no tiene binding.
- Marcha: reversa en rojo y punto muerto en azul cuando estan activas.
- Corregido: punto muerto usa el azul del testigo de luces altas y reversa
  usa el rojo del freno de estacionamiento.
- Eliminada la accion `cruiseresume` (Crucero: reanudar).
- Estas acciones usan auto-binding de teclas libres si el perfil no tiene
  binding; en ATS se asignan Alt+tecla para no pisar bindings existentes.
- Corregido: StreamDock filtra por `Knob` (no por `Encoder`); las tres
  acciones ya aparecen en la pestana **Rueda** del software Ajazz.
- Iconos actualizados con `New Icons`: 37 iconos `pk-*`/`tile-*` reemplazados.
  Backup completo en `backups/icons-20261104-231303` con `icon-map.txt`.
- Limpiaparabrisas ahora ocupa todo el tile, con el estado en etiqueta inferior.
- Radio rediseñado con encuadre (borde redondeado) como los demás iconos.
- Radio: icono reducido un 20% adicional y encuadre con grosor doble.
- Motor usa `engine_start_2` / `engine_stop_2`.
- Todos los pulsos se unifican al estilo de balizas (alpha sobre icono `-c`).
- Estacionarias ahora usa el icono `hazard` en el tile (antes mostraba flechas).
- Corregido pulse de los 6 testigos recién creados (cámaras y marchas): ahora
  re-renderizan cada 100 ms como balizas.
- Radio y crucero: icono apagado oscuro y encendido en color con pulse.
- Radio rediseñado con encuadre redondeado y contenido reducido un 20%.
- `dialRotate` y `dialDown` atendidos por el backend; la entrada se encola en
  `scsmon-input` y no bloquea el render.
- Crucero: `cruiectrlinc` / `cruiectrldec`, pulso = `C`.
- Retarder: `retarderup` / `retarderdown`, pulso = `B` (freno motor).
- Volumen: teclas multimedia de Windows, pulso = silencio.
- Si el perfil no tiene tecla para crucero/retarder, se agrega una tecla
  libre al cerrar el juego (backup `.hs-window-backup`, sin pisar bindings).
- Property Inspector `encoder.html`: invertir giro y teclas por muesca.
- Nuevo modo de diagnostico `-NoInput` para probar eventos sin inyectar teclas.

## [1.1] - 2026-10-29

### Nuevas características y acciones
- **Radio del juego (`radiobtn`)**: Control y testigo de radio con icono dedicado (`audio-player.png` / `-c`).
- **Encendido/Apagado del motor (`enginebtn`)**: Testigo y control de motor con iconos dedicados (`engine.png` / `engine-c.png`).
- **Check Engine (`checkengine`)**: Indicador de fallos de motor activo solo ante advertencias críticas.
- **Autonomía con motor encendido (`enginerange`)**: Muestra la autonomía restante solo cuando el motor está en marcha.
- **Marcha con motor encendido (`enginegear`)**: Número de marcha exclusivo en blanco (avance), azul (neutral) y rojo (reversa) condicionado al motor encendido.
- **Acople de tráiler (`trailerattach`)**: Integración con iconos oficiales de acople (`coupling.png` / `-c`) y enlace a la tecla `T`.
- **Ventanillas (Izquierda y Derecha)**: Control unificado por ventana con toque rápido para subir (`ñ` / `.`) y mantener presionado para bajar (`{` / `-`), con indicador visual en blanco (cerrada) y cyan estático (abierta).
- **Freno de estacionamiento**: Actualizado con el icono oficial `parking-brake-1` y variante activa.
- **Soporte Panel AKP153E**: Añadido el controlador `"Information"` en todas las acciones para permitir su colocación tanto en el teclado como en la pantalla auxiliar vertical del AKP153E.

## [1.0] - 2026-10-14

### Corregido (P0)

- Presion de aire en **PSI** (antes se rotulaba `bar`).
- Accion `engine`: muestra el estado real del motor con icono propio;
  `O_MOTOR` es el freno motor, no el encendido.
- `checkengine`: requiere motor encendido y desgaste >= 5 %, igual que la app
  Windows; ya no se dispara por combustible/aire. Umbral validado con un
  `Snap` sintetico: 4,0 % apagado; 5,0 % y 6,0 % encienden; motor apagado,
  telemetria inactiva o desgaste nulo quedan apagados.
- Freno de estacionamiento (antes "freno de emergencia") y testigo `park`
  leyendo el freno real en vez de las luces de posicion.
- Ventanillas por **VK + scan** (independientes del layout y aceptadas por
  ETS2; el scan-only se ignora).
- Un solo `StringFormat` compartido: elimina la fuga de handles GDI+.
- `willDisappear` elimina contextos y tiempos de pulsacion.

### Rendimiento (P1.1)

- Render bajo demanda: cada tile guarda una clave de su estado visible; si no
  cambio, se reutiliza la ultima imagen y se omite dibujar, codificar PNG y
  base64. Las animaciones de pulso usan buckets de 100/250 ms.
- Medido con el juego pausado: **7,71 % -> 2,19 % de un nucleo** y 41,1 -> 39,4
  MB de working set (~72 % menos CPU en reposo).

### Rendimiento (P1.2)

- Iconos `pk-*.png` preescalados a 72x72 al cargar y tiles de icono
  precomputados en base64: `RenderPack` y `hazbtn` dejan de dibujar y codificar
  por frame. Los iconos que se dibujan a 44x44/32x48 conservan el original.
- Medido con baliza, hazard, intermitentes, trailer y freno de estacionamiento
  activos: **2,19 % -> 1,07 % de un nucleo** (acumulado P0: 7,71 % -> 1,07 %,
  ~86 % menos) y 40,5 MB de working set.
- Medido conduciendo 180 s: **0,80 % de un nucleo** (1,438 s) y 46,3 MB;
  velocidad 49-116,7 km/h y RPM 1.086-1.918, sin errores de render. No hay
  indicio de que P1.3-P1.5 sean necesarios.
- La clave de render incluye el nombre de la metrica para evitar colisiones.

### Visualizacion (P2)

- RPM con barra de avance de 60x4 px, color por zona y ultimo 10 % en rojo;
  el numero conserva 18 pt y "CAMBIA" baja a 11 pt.
- Gauges de agua, aceite y aire con anillo de 270 grados, zona de peligro,
  marca de umbral y progreso coloreado; mismas condiciones de aviso.
- Ruta con km restantes a 19 pt, tiempo restante, barra de avance y
  recorrido/plan;
  danos con iconos atenuados, barra de severidad proporcional y 100 % a 12 pt.
- Crucero con icono mas visible, fondo mas oscuro y pildora de estado
  verde/ambar/rojo (`OK`/`FUERA`/`OFF`).
- Un solo helper de pulso (coseno de 1 s, alfa 0,35-1,0) y paleta por
  severidad; el exceso de limite conserva un parpadeo urgente de 2 Hz.
- Desplegado y medido con el juego pausado: **0,97 % de un nucleo** y 40,9 MB
  de working set (26 contextos activos en dos dispositivos).
- Revalidado en el build final con `Snap` sintetico: ruta 350/451 y sin viaje,
  danos 9/2/1 y 100/60/21, crucero `OK`/`FUERA`/`OFF`, limite 80/90/sin
  limite, RPM 1500/1850 con `CAMBIA` y gauges normal/aviso. Sin recortes.

### Ventanillas automaticas

- El plugin detecta el perfil activo de cada juego desde el ultimo
  `New profile selected:` / `Set profile finished:` de `game.log.txt`
  (nombre -> carpeta hex) y agrega solo las acciones `lwin*`/`rwin*` que no
  tengan tecla. No pisa bindings del usuario.
- Antes de escribir guarda `controls.sii.hs-window-backup`; solo parchea con el
  juego cerrado (SCS lee `controls.sii` al arrancar) y un watcher reintenta
  cada 15 s cuando el juego se cierra.
- Perfil ETS2 actual: `F8` subir izq, `Alt+F8` bajar izq, `Alt+coma` subir der,
  `Alt+menos` bajar der. ATS conserva sus defaults `slash`/`period`/
  `apostrophe`/`semicolon`. Los bindings se leen del perfil en runtime.
- La tecla se inyecta con VK+scan y se mantiene 1,4 s en un hilo aparte: las
  acciones de ventanilla SCS son "mientras se mantiene" y un down+up
  instantaneo no las mueve. Toque = subir; mantener >400 ms = bajar.
- Diagnostico: `backend.exe -WindowsInfo` y `backend.exe -WindowsFix`.
- Validado en ETS2 por inyeccion directa y con los **botones fisicos** del
  StreamDock (izquierda/derecha, toque/mantener), sin efectos laterales
  reportados. `backend.log` registra `mantenida 1400ms` con foco en el juego.

### Validacion en vivo

- ETS2 conduciendo 180 s: **0,80 % de un nucleo** (1,438 s), 46,3 MB, con
  velocidad 49-116,7 km/h y RPM 1.086-1.918; sin errores de render.
- Confirmado por el usuario en el StreamDock fisico: RPM/marcha,
  velocimetro/gauges, ruta/danos, limite 80 en alerta, crucero `OK`/`FUERA`,
  motor, PSI y freno de estacionamiento.
- `checkengine` validado con `Snap` sintetico en 4/5/6 % y las ventanillas con
  los botones fisicos. No quedan pendientes de P0, P1.1-P1.2 ni P2; P1.3-P1.5
  siguen en backlog marginal.

### Pendiente

- P1.3 (`SendRaw`), P1.4 (`FlashWatch`) y P1.5 (logging) en backlog por impacto
  marginal; P3 paridad macOS/knobs.

## [1.0] - 2026-10-14

Primera version publica ("Ajazz Truck Simulator").
- Instalador Inno Setup con deteccion Steam + carpeta manual + desinstalador.
- Property Inspector en espanol.
- Docs (instalacion, compatibilidad, desarrollo) + imagenes con datos en vivo.
- Repo git solo-fuentes.

## Historial interno (previo a 1.0)

- Instalador Inno Setup con deteccion Steam + carpeta manual (`installer/`).
- Property Inspector unificado en espanol.
- Repo git: solo fuentes (sin exe/dll/logs).
- Docs: instalacion, compatibilidad, desarrollo + imagenes.

## [3.0] - 2026-10-13

- 21 acciones: monitores + botones (limpiaparabrisas, diferencial, freno
  emergencia, temperaturas, presion de aire).
- Backend C# nativo (`backend.exe`, sin dependencias), auto-reconexion del
  MMF si el plugin arranca antes que el juego.
- Vigilante de rafagas (muestreo 15 ms + latch 1.5 s) y modo `-WatchHigh`.
- Tiles redisenados; crucero con set visible; ruta con recorrida/faltante/ETA.
