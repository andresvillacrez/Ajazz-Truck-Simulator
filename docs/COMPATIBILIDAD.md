# Compatibilidad de dispositivos

Las acciones de teclas usan **`Keypad` estandar** del SDK
StreamDeck/StreamDock y cualquier dispositivo con teclas LCD las acepta
**sin cambios**. Las acciones de encoder usan `Controllers: ["Knob"]` y
solo aparecen en dispositivos con mandos giratorios (AKP03E/AKP05), dentro de la pestana **Rueda**.
El software escala los PNG al tamano de cada tecla o pantalla.

## Matriz

| Dispositivo | Teclas LCD | Extras | Estado |
|---|---|---|---|
| Ajazz AKP153R / AKP153E | 15 (3×5), ~85 px | — | **Probado**: todo funciona |
| Ajazz AKP05 / AKP05E | 10 (2×5), ~112 px | 4 knobs + tira tactil | Compatible: teclas + encoders (crucero/retarder/volumen) |
| Ajazz AKP03 / AKP03E / Mirabox N3 | 6 LCD | 3 encoders | Compatible: teclas + encoders (no probado en hardware aqui) |
| Otros Keypad StreamDock | LCD | — | Compatible por construccion |

## Notas por dispositivo

- **AKP05/E y AKP03E**: los PNG se reescalan al tamano de cada tecla. Los
  knobs generan eventos Encoder (dialRotate/dialDown) y ya tienen
  acciones dedicadas: crucero, retarder y volumen. En AKP03E el pulso del
  encoder de crucero activa/desactiva el control (C), el de retarder usa el
  freno motor (B) y el de volumen silencia. Si faltan teclas de crucero o
  retarder, el plugin agrega una libre al cerrar el juego (sin pisar las del
  usuario) y hay que esperar unos segundos y reiniciar el juego una vez.
- **Tira tactil / touch strip**: las acciones de encoder usan el layout
  $B1 para titulo, valor e indicador cuando el dispositivo tiene pantalla
  tactil; en AKP03E los mandos no tienen pantalla propia.

## Como probar en un dispositivo nuevo

1. Instala con el instalador (vale para cualquier dispositivo).
2. Arrastra 3-4 acciones (p. ej. Velocidad, RPM, Largas-boton, Ruta).
3. Con el juego abierto y sin pausa, verifica refresco y pulsacion.
4. Anota el resultado en la tabla de arriba y abre issue/PR si algo falla.
