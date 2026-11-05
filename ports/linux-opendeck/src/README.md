# Backend Linux nativo (diseño)

Esta carpeta está reservada para la segunda fase del port Linux: un backend
nativo que no dependa de Wine. La v0 actual usa `backend-linux.sh` + Wine.

## Objetivo

```text
SCS plugin puente  ──socket──>  backend Linux nativo  ──WebSocket──>  OpenDeck
```

## Componentes

| Módulo | Responsabilidad |
|---|---|
| `telemetry` | Leer snapshots del socket y mantener el estado actual |
| `actions` | Mapa UUID → tipo de acción (monitor, botón, encoder) |
| `render` | Dibujar tiles 72×72/112×112 y codificar PNG/base64 |
| `protocol` | WebSocket Stream Deck/OpenDeck (`registerPlugin`, `setImage`, `keyDown`, etc.) |
| `input` | Inyectar teclas: `uinput`, `ydotool` o XTest |
| `main` | Ciclo de vida, argumentos `-port`/`-pluginUUID`, logs |

## Fuente de telemetría

Dos opciones, en orden de preferencia:

1. **Plugin puente propio** (DLL Windows dentro de Proton) que lea la memoria
   compartida del SDK SCS y publique JSON por un socket TCP/Unix. Es la opción
   más robusta y desacopla el juego de OpenDeck.
2. **Helper Wine** que lea `Local\SCSTelemetry` y reenvíe por socket. Permite
   reutilizar los offsets REV12 actuales sin escribir una DLL nueva.

Formato sugerido (JSON Lines, 10–20 Hz):

```json
{"t":1730000000,"active":true,"speed":24.3,"rpm":1450,"gear":12,"fuel":310.2,
 "range":820,"limit":80,"cruise":24.0,"damage":{"truck":4,"trailer":1,"cargo":0},
 "route":{"remaining":350000,"eta":2760,"planned":451000},
 "lights":{"low":true,"high":false,"beacon":false,"brake":false},
 "temps":{"water":86,"oil":91,"air":132}}
```

## Protocolo OpenDeck

- OpenDeck inicia el plugin con `-port <puerto> -pluginUUID <uuid>
  -registerEvent <evento>`.
- El backend abre un WebSocket a `127.0.0.1:<puerto>` y envía
  `registerPlugin`.
- Eventos: `willAppear`, `willDisappear`, `keyDown`, `keyUp`, `dialRotate`,
  `dialDown`, `sendToPlugin`.
- Salidas: `setImage`, `setTitle`, `setState`, `showAlert`, `logMessage`.

## Render

- 72×72 px base (OpenDeck escala al dispositivo).
- Cachear PNG/base64 por `StateKey`, igual que el backend C#.
- Fuentes: Arial/Roboto/DejaVu; incluir una fuente para no depender del sistema.
- Soportar tema oscuro/claro y alto contraste.

## Entrada

- X11: XTest (`python-xlib` o `libXtst`).
- Wayland: `uinput` (requiere permisos) o `ydotool`.
- Alternativa: `wtype` para Wayland.
- Configurar el método con `SCSMON_INPUT=xtest|uinput|ydotool`.

## Empaquetado

- Python: `pyinstaller --onefile` + `requirements.txt`.
- Rust: binario estático + `openaction`/`tungstenite`/`image`.
- Incluir `.desktop`, icono y script de instalación en OpenDeck.

## Pruebas

- Simulador que emita snapshots JSON desde un archivo.
- Golden PNG por acción/estado.
- Prueba de protocolo con un servidor WebSocket falso.
- Prueba de entrada en X11 y Wayland.

## Criterio de aceptación

- Mismas 32 acciones y UUID que Windows/macOS.
- 60 FPS no es necesario; 10–20 Hz de actualización y render bajo demanda.
- CPU < 2 % de un núcleo en reposo.
- Sin dependencia de Wine en la fase 2.
