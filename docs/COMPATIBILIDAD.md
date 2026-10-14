# Compatibilidad de dispositivos

El plugin usa acciones **`Keypad` estandar** del SDK StreamDeck/StreamDock:
cualquier dispositivo con teclas LCD las acepta **sin cambios** — el software
escala los PNG de 72 px al tamano de cada tecla.

## Matriz

| Dispositivo | Teclas LCD | Extras | Estado |
|---|---|---|---|
| Ajazz AKP153R / AKP153E | 15 (3×5), ~85 px | — | **Probado**: todo funciona |
| Ajazz AKP05 / AKP05E | 10 (2×5), ~112 px | 4 knobs + tira tactil | Compatible en teclas; knobs sin acciones (pendiente de definir mapeo) |
| Ajazz AKP03 / Mirabox N3 | 6 LCD | 3 encoders | Compatible en teclas (no probado aqui) |
| Otros Keypad StreamDock | LCD | — | Compatible por construccion |

## Notas por dispositivo

- **AKP05/E**: los PNG se reescalan de 72 → ~112 px (leve perdida de
  nitidez, aceptable). Los knobs generan eventos `Encoder`
  (`dialRotate`/`dialDown`): el backend hoy solo atiende `keyDown`, asi que
  no hacen nada hasta que se definan acciones (candidatos: crucero +/−,
  volumen, retarder).
- **Tira tactil / touch strip**: fuera de alcance (requiere acciones
  dedicadas del SDK).

## Como probar en un dispositivo nuevo

1. Instala con el instalador (vale para cualquier dispositivo).
2. Arrastra 3-4 acciones (p. ej. Velocidad, RPM, Largas-boton, Ruta).
3. Con el juego abierto y sin pausa, verifica refresco y pulsacion.
4. Anota el resultado en la tabla de arriba y abre issue/PR si algo falla.
