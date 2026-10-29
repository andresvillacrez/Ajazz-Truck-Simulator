# Plugin ATS - ETS Ajazz — Backup e historial
Fecha: 2026-10-13. Equipo: i7-9700F + RX 6600 8GB + 32GB. Usuario: hs-se (HS-GAMING).

## Contenido de este backup
- `plugins/com.hsgaming.scsmon.sdPlugin/` — Plugin "Trucker Simulator" v3.0 (22 acciones).
- `automatizacion/SCS_AutoGaming/` — Watcher (pausa OneDrive al jugar) + arranque automático.
- `automatizacion/Modo_Gaming_SCS.bat` — Lanzador manual ATS/ETS2 (solo OneDrive).
- `dll/scs-telemetry.dll` — RenCloud v1.12.1 Win64 (instalada en ambos juegos).
- `HISTORIAL.md` — este archivo.

## Trucker Simulator (StreamDock/Ajazz AKP153E)
- Ruta instalada: `%APPDATA%\HotSpot\StreamDock\plugins\com.hsgaming.scsmon.sdPlugin\`
- Backend: `backend.exe` (C#, `backend.cs`, compila con csc Framework64, sin dependencias).
  Antes fue PowerShell+stub: se descartó porque el host cortaba la conexión cada 2-4 s.
- Fuente de datos: MMF `Local\SCSTelemetry` (RenCloud scs-telemetry.dll en
  `Steam\steamapps\common\{American Truck Simulator,Euro Truck Simulator 2}\bin\win_x64\plugins\`).
- Layout MMF (scs-telemetry-common.hpp REV12, offsets bytes verificados en vivo):
  sdk=0, paused=4, game=52(u32:1 ETS2/2 ATS), speed=948f, rpm=952f, gear=504i32,
  gearDash=508i32, fuel=1000f, fuelCap=704f, fuelRange=1008f(km), cons=1004f(L/km),
  cruise=988f, limit=1068f, rpmMax=740f, park=1566b, motor=1567b,
  blinkLact=1578b, blinkRact=1579b, blinkL=1580b, blinkR=1581b, parking=1582b,
  low=1583b, high=1584b, beacon=1585b, brake=1586b, reverse=1587b, hazard=1588b,
  cruiseOn=1589b, fuelWarn=1570b, planned=100u32(km), routeD=1060f(METROS restantes),
  routeT=1064f(segundos de juego), onJob=4300b, odometro=1056f,
  wears camion=1036..1052f, trailer0: attached=6080b, cargoDmg=6152f, chasis=6156f,
  ruedas=6160f, carroceria=6164f, jobCargoDmg=1468f.
- Correccion 2026-10-27: `motor` (1567) es **motorBrake** (freno motor), no el
  motor encendido; el encendido real es `engineEnabled` (1576). La presion de
  aire (992) esta en **PSI** (rango real ~120-145), no en bar.
- OJO off-by-one histórico: hazard es 1588 (1587 es reversa). Ya corregido en backend.cs.
- routeD viene en METROS (dividir /1000). routeT en segundos de juego.
- Acciones (22): monitor, speed(km/h siempre), gear, rpm(zonas 60/75/90% + CAMBIA),
  fuel(icono+barra+autonomia), range, cons(L/100km), cruise, limit, turn, beams,
  auto, brake, blinkL, blinkR, hazard, lowB, highB, park, lights, damage, route,
  hazbtn(Estacionarias: doble flecha + pulso + manda F),
  blinkLbtn(manda scan 1A `[`), blinkRbtn(scan 1B `]`), lightbtn(manda L),
  highbtn(manda K), beaconbtn(manda O, pulso 1 s).
  Contar UUID reales en manifest.json (esta lista puede variar).
- Botones (keyDown): F=0x46 hazard, L=0x4C luces, K=0x4B largas, O=0x4F baliza (VK),
  `[`/`]` por scan 0x1A/0x1B + VK (VK+scan; el layout español rompe el VK OEM puro).
  Estructura INPUT x64 = 40 bytes (¡24 falla!). El juego debe estar enfocado.
- Hazard debe estar asignado a F en Opciones→Teclas (estaba sin asignar).
  L, K, O, `[`, `]` vienen asignados por defecto.
- Refresco: loop ~0.15 s + solo reenvía si la imagen cambia. Vigilante de ráfagas:
  hilo que muestrea altas cada 15 ms con memoria de 1 s (log "rafaga detectada").
- Iconos de testigos: pack `jp.tsuteto.ets2.sdIconPack` (blanco=off, -c=color=on).
- Icono del paquete: logo ATS de `Steam\appcache\librarycache\270880\logo.png`.
- `backend.exe -Test` vuelca valores + PNGs a `%TEMP%\scsmon-test`.
- `backend.log` en la carpeta del plugin (bitácora con tope 200 KB).

## Automatización gaming SCS
- `Watch-SCS.ps1` + `Start-SCS-Watcher.vbs` vigilan amtrucks/eurotrucks2:
  al jugar cierran SOLO OneDrive; al salir lo restauran. (Antes tocaba OBS/Discord:
  el usuario los necesita para streaming, NO tocar.)
- Arranque: HKCU `...\Run\SCS_AutoGaming`. Tarea `OBS AutoStart` (logon, visible).
- Steam launch opts ATS/ETS2: `-nointro -mm_pool_size 12000 -mm_max_resource_size 32 -mm_max_tmp_buffers_size 1000`.

## ATS/ETS2 optimizados (1080p75, Ultra estable)
- ATS config: buffer 30, escala 150%, sombras 4096/3, vsync 0, AF 16x, cap 75,
  averaging 60/120, espejos 2/100 m, lluvia 2, lod tráfico/aparcados 0.8.
- ETS2 igual + espejos escala 1, hierba/veg 1, SSAO 2.
- GameDVR off, GameMode on, HAGS on. Cachés limpiados.
- Perfiles FUERA de OneDrive: junctions
  `OneDrive\Documentos\{ATS,ETS2} -> C:\Users\hs-se\Documents\{...}`.
  Respaldo ATS borrado; queda `Euro Truck Simulator 2.syncbak` (borrar tras probar).

## Windows/driver (RX 6600)
- Plan Máximo rendimiento, GameDVR off. HAGS=2 on.
- PENDIENTE ADMIN: `AMD Crash Defender→Manual`, MPO `OverlayTestMode=5`,
  ULPS `EnableUlps=0`, prioridades multimedia Games, Ultimate Performance.
  NO tocar AMD External Events Utility.
- Print Spooler estaba Disabled → poner Automatic (Word no veía impresoras).
- Instalar `Microsoft Print to PDF` (feature, admin).

## Issues ABIERTOS (importante)
1. **Ráfagas (flasher)**: el juego SÍ las publica (@1584) y el vigilante las captura
   ("rafaga detectada" en log), pero el usuario reporta que el testigo no enciende.
   Último estado: latch 1 s + backend C# estable. Verificar con el usuario mirando
   la tecla Largas/botón al hacer ráfagas de 1-2 s.
2. **Enlace Ajazz**: el software abre el dispositivo con `handle = 0x0` y reintenta
   willAppear cada ~4 s. Teclas responden al pulsar (entrada OK); si la pantalla
   no refresca, revisar USB (cable/puerto directo, sin hub) con software cerrado.
3. **PDH roto**: contadores clásicos vacíos (incluso tras `lodctr /R` + reboot).
   Solo vive el proveedor GPU. Plugin de monitoreo Ajazz (mirabox) depende de PDH.
   Ruta de reparación: `sfc /scannow` → DISM → upgrade in-place. El usuario probó
   otro plugin de monitoreo y lo resolvió por su lado.
4. **GoW3 port** `C:\GAMES\God of War III Remastered` actualizado a release
   `windows-v2.1.8` (SHA256 verificado). Saves en `Partidas Guardadas` + backup en
   `C:\GAMES\backup-pre-2.1.8`. Mostraría versión juego 01.00 → fijar Update folder
   a `Juego\CUSA01623-patch` en el lanzador para 01.02.
5. Controles SCS: hazard sin asignar por defecto (asignar F); `[`/`]` y L/K/O sí.
   Mando GameSir presente (XInput + DirectInput); OBS para streaming.
6. Carpeta de respaldo ATS `.syncbak` ya borrada; ETS2 `.syncbak` pendiente.

## Sesiónsprevias
- Modelo anterior: Muse Spark (finito). Migración a omniroute/OpenCode en curso.
- Para retomar: este backup + pegar este historial en la nueva sesión.

## Actualizaci�n 2026-10-13 (continuaci�n de sesi�n)

### Plugin Trucker Simulator
- Backend nativo `backend.exe` en C# con fuente `backend.cs`; compilado localmente con Framework csc.
- Se copiaron a la carpeta de proyecto `plugins\com.hsgaming.scsmon.sdPlugin` y el pack ETS2 `iconos\jp.tsuteto.ets2.sdIconPack`.
- Se a�adieron acciones: limpiaparabrisas, bloqueo diferencial, freno emergencia y gauge de temperatura de motor/aceite y presi�n de aire.
- Eliminadas acciones `Monitor libre`, `Crucero velocidad y tolerancia`, `Autonom�a km`.
- Varios iconos tile redise�ados; velocidad veloc�metro, RPM con marcha/CAMBIA, combustible surtidor+barra, da�os y ruta con iconos, crucero con n�mero/set y testigo estado, l�mite en c�rculo que pulsa naranja/rojo al exceder.
- Botones probados: emergencia Space, turn left/right SCAN 1A/1B, luces L, largas K, baliza O, limpiaparabrisas P (ciclo local OFF/A/2/3; game telemetry solo publica on/off), diferencial V. Input SendInput struct debe ser 40 bytes (explicit layout x64).
- Baliza usa O y pulso. Intermitentes de emergencia F; altas: vigilante muestrea offset 1584 pero el juego no mostr� destellos al hacer r�fagas en algunas pruebas. Requiere retomar test.
- Freno emergencia: icono original `emergency-brake.png`, blanco apagado / pulso rojo activo; bot�n Space.
- Route: planned km offset 100, routeD metros offset1060, routeT segundos juego offset1064; recorrido=planned-routeD/1000. ETA. Numerales separados, recorrido verde y faltante cyan.
- Crucero: `cruise` button manda C, icono control crucero con n�mero set verde siempre visible; icono 80% transparencia y texto CRUCERO. Bot�n muestra testigo verde pulsante cuando CC on. Velocidad set se convierte m/s*3.6, como el dump ETS2 comprobado (14.31 m/s ~= 52 km/h).
- Nuevo render `cruiseSpeed` se conserv� en backend pero la acci�n fue retirada del manifest por el usuario.
- Nuevos gauges: water temp offset1024, oil temp offset1020, aire 992; warning water threshold offset732, air warning 720/emergency724. Aceite rojo >=130C porque SDK no publica threshold aceite.
- Tile ruta/da�o recreados con make-icons.ps1. Pack de iconos del Ajazz ya respaldado en `iconos`.
- �ltima acci�n de usuario: �actualiza la carpeta del proyecto�; copia completada; verificar/actualizar este historial despu�s de cambios futuros.

## Fix 2026-10-14 � auto-detecci�n de ATS/ETS2 tras iniciar el plugin antes del juego
- S�ntoma: Stream Dock/backend arrancaban al iniciar sesi�n y se quedaban con MMF Local\SCSTelemetry null si el juego todav�a no hab�a abierto; no reconectaban.
- Fix en `plugins\com.hsgaming.scsmon.sdPlugin\backend.cs`: backend sondea y abre MMF cada 1 s cuando no hay snapshot activo; si el juego cierra / el SDK deja de estar activo, libera accessor y MMF, entra en espera y vuelve a detectar el siguiente arranque. El hilo FlashWatch tambi�n espera y se conecta/reconecta independientemente.
- Compilado y desplegado a AppData + backup `E:\Proyectos de software\Plugin ATS - ETS Ajazz\plugins\com.hsgaming.scsmon.sdPlugin\backend.exe` (hash iguales).
- Verificado 14-oct: `backend.exe` vivo; log `mapa Local\SCSTelemetry conectado`; ETS2 log confirma carga de scs-telemetry.dll; MMF sdkActive=1 y pause=1 (estar en pausa solo congela cambios, no es fallo de detecci�n).

## Fix 2026-10-14 (2) - falso positivo rafaga + diagnostico
- Sintoma: log mostraba 'rafaga detectada' ~20ms despues de inicio, ANTES de mapa conectado. Causa: FlashWatch leia O_HI=1584 sin comprobar O_SDK=0; MMF retenia basura de sesion anterior.
- Fix backend.cs: FlashWatch ignora muestras con sdk==0; solo actualiza latch con sdk==1 (log 'rafaga detectada sdk=1 hi=1' / 'rafaga fin hi=0'). Reintento 1s. Latch HighShown 1.0s -> 1.5s.
- Nuevo modo backend.exe -WatchHigh: muestrea sdk/pausa/LO/HI cada 15ms 60s.
- Compilado csc + desplegado (StreamDock reiniciado). SHA256 nuevo 7ce1b80e. Log nuevo sin falso positivo.
- Verificado 11:19: ETS2 vivo PID 6240 pausado 108km/h M12, backend PID 11824 conectado.
- PENDIENTE USUARIO: con ETS2 enfocado hacer rafagas 1-2s, mirar boton Largas + correr -WatchHigh para confirmar HI 0->1 en 1584.


## Cierre Issue 1 (2026-10-14) - Rafagas
- Usuario confirmo dejarlo hasta ahi sin mas cambios. Telemetria verificada con -WatchHigh: HI 0->1 en 1584 al quitar pausa y togglear largas (ciclos 7.6s/9.3s/10.5s/12.0s, latch shown=1 OK).
- Causa del reporte inicial: prueba con pau=1 (telemetria congelada) + falso positivo de arranque (ya fixeado). Se declara TERMINADO.


## Cierre Issue 2 (2026-10-14) - Enlace Ajazz
- Verificado: deviceDidConnect 11:19:37, sin bucle willAppear, 0 errores USB/HID 24h, dispositivos en puertos directos. Usuario confirma pantalla refresca + teclas responden. TERMINADO sin cambios.


## Cierre Issue 3 (2026-10-14) - PDH
- PDH sigue roto (typeperf Processor = contadores no validos). Usuario confirma resuelto con plugin alternativo. No se toca el OS. TERMINADO.


## Cierre Issue 4 (2026-10-14) - GoW3 Update folder
- Verificado settings.json: game_dir=CUSA01623 (base 01.01), update_dir=CUSA01623-patch (APP_VER 01.02, eboot.bin presente). El lanzador ya lee version del patch. No hizo falta cambio. TERMINADO.


## Cierre Issue 5 (2026-10-14) - Controles SCS
- Usuario confirma bindings correctos: F=hazard (asignado manual), scan 1A/1B intermitentes, L/K/O, P, V, Espacio, C. K verificado en vivo con WatchHigh. TERMINADO.


## Cierre Issue 6 (2026-10-14) - Respaldo ETS2
- Borrado OneDrive\Documentos\Euro Truck Simulator 2.syncbak (19MB, 12/10, obsoleto). Perfil activo en C:\Users\hs-se\Documents guarda bien (verificado 11:31 con juego en marcha). Junctions ATS/ETS2 OK. TERMINADO.


## GitHub 2026-10-14 - v1.0 publicado
- Repo creado: https://github.com/andresvillacrez/Ajazz-Truck-Simulator (publico). Rama main, 3 commits, tag v1.0. Identidad andresvillacrez@gmail.com.


## Release v1.0 en GitHub
- Release publicado: tag v1.0 + asset TruckerSim-Ajazz-Setup-1.0.exe (2.12 MB).


## Actualizacion 2026-10-27 - P0/P1/P2
- P0 correctitud: presion de aire en PSI, estado real del motor (`motorBrake`
  vs `engineEnabled`), `checkengine` con desgaste >= 5 %, freno de
  estacionamiento real, ventanillas por VK+scan con bindings automaticos,
  `StringFormat` compartido
  y limpieza de contextos en `willDisappear`.
- P1 rendimiento: render bajo demanda por clave de estado e iconos
  preescalados/tiles cacheados. CPU con el juego pausado: 7,71 % -> ~1 %.
- P2 visualizacion: barra de RPM, gauges con anillo, ruta/danos jerarquizados,
  crucero con contraste y pulso unificado por severidad. Medido: 0,97 % de un
  nucleo y 40,9 MB con 26 contextos activos en dos dispositivos.
- Desplegado SHA256 `CD611A4F6A4C29DC9515F1E65574AA6C7DFEBCECB2CB8BC8A9682481CFA89F35`,
  backend PID `21144`, sin errores en `backend.log`.

## Ventanillas automaticas 2026-10-27

- Objetivo: subir/bajar ventanillas sin asignar teclas a mano, conservando
  toque = subir y mantener = bajar.
- Perfil activo de cada juego: ultimo `New profile selected:` /
  `Set profile finished:` de `game.log.txt`; nombre -> hex
  (`gamercofla` -> `67616D6572636F666C61`).
- `EnsureWindowBindings` / `-WindowsFix` agregan solo `lwin*`/`rwin*` sin tecla,
  crean `controls.sii.hs-window-backup` y no tocan bindings existentes. No se
  parchea con el juego abierto; `WindowBindingWatch` reintenta cada 15 s.
- Perfil ETS2 parcheado (game cerrado, backup 43482 bytes):
  - `lwinclose` = `modifier(no_modifier, keyboard.f8?0)`
  - `lwinopen` = `modifier(alt_only, keyboard.f8?0)`
  - `rwinclose` = `modifier(alt_only, keyboard.comma?0)`
  - `rwinopen` = `modifier(alt_only, keyboard.minus?0)`
  - ATS sin cambios: `slash`/`period`/`apostrophe`/`semicolon`.
- Causa raiz de "ninguna funciono": ETS2 ignora `SendInput` con
  `KEYEVENTF_SCANCODE` puro. `SendScan` ahora calcula VK con `MapVirtualKey` y
  envia VK+scan (probado con luz de cruce: `PressScan 0x26` la alterna).
- Las acciones de ventanilla son "mientras se mantiene": `PressScanHold`
  mantiene la tecla principal 1,4 s en un hilo aparte (Alt se suelta a los
  ~100 ms; el juego ya engancho la accion). Toque <400 ms sube; mantener baja.
- Validado por inyeccion directa con capturas y con los **botones fisicos**
  del StreamDock el 27/10/2026: izquierda y derecha abren y cierran, toque y
  mantener, sin efectos laterales reportados. `backend.log` confirma
  `mantenida 1400ms` con foco `eurotrucks2`.
- Desplegado SHA256 `2CEB60CADD276DE07DC0926C94E8FC1F841C869A70994975C1C39D21EBF610C8`,
  backend PID `22084`, StreamDock PID `7764`, ~44,7 MB. `backend.log` confirma
  perfil activo ETS2 y los cuatro bindings.

## Validacion checkengine 2026-10-27

- Arnés de reflexión sobre el build desplegado: se crea `SCSMon+Snap`
  sintético y se invoca el render privado `RenderCheckEngine`; el PNG se
  inspecciona píxel a píxel.
- Resultados:
  - `active=true`, `engine=true`, desgaste 4,0 % -> PNG negro (261 bytes,
    0 px no negros).
  - desgaste 5,0 % y 6,0 % -> `CHECK ENGINE` rojo (1017 bytes, 1001 px no
    negros).
  - motor apagado, `active=false` o `engineWear=null` -> PNG negro.
- Confirma el umbral >= 5 % de la app Windows, sin falsos positivos por
  combustible, temperatura o aire. Evidencia en
  `%TEMP%\opencode\checkengine-*.png`.

## Validacion sintetica de tiles 2026-10-27

- Sobre el build `2CEB60...` se crearon `Snap` sintéticos y se invocó el
  render privado `RenderKey`/`RenderCheckEngine`, guardando PNGs en
  `%TEMP%\opencode\syn-*.png` y `checkengine-*.png`.
- Casos cubiertos: ruta 350/451 y sin viaje; daños 9/2/1 y 100/60/21;
  crucero `OK`/`FUERA`/`OFF`/sin velocidad; límite 80/90/sin límite; RPM
  1500 (naranja) y 1850 (`CAMBIA` rojo); gauges de agua, aceite y aire en
  normal y aviso; `checkengine` 4/5/6 %.
- Todos los tiles son legibles y no se recortan; no se requirieron cambios de
  código. La validación en vivo se confirmó en la sección siguiente.

## Medicion en conduccion 2026-10-27

- ETS2 en marcha, 36 muestras cada 5 s (180 s): CPU del backend 1,438 s =
  **0,80 % de un núcleo**; working set medio 46,3 MB (máximo 46,5 MB).
- Telemetría observada: velocidad 49,0-116,7 km/h, RPM 1.086-1.918, crucero
  activo en 6/36 muestras (68 km/h fijados).
- Nota de diagnóstico: el primer sampler leyó `gear` con `ReadSingle` y mostró
  0; el backend usa `ReadInt32` (como corresponde) y el juego marcaba 14. No es
  un bug del plugin. Confirmado también `gearDash=14`.
- Sin errores `render:`/`ev-err:` en `backend.log` durante la prueba. Con este
  consumo, P1.3-P1.5 siguen en backlog sin urgencia.
- Confirmado por el usuario en el dispositivo físico: RPM/marcha,
  velocímetro/gauges, ruta/daños, límite 80 en alerta, crucero `OK`/`FUERA`,
  motor, PSI y freno de estacionamiento. Con esto quedan cerrados P0,
  P1.1-P1.2 y P2; solo quedan P3 y el backlog marginal P1.3-P1.5.

