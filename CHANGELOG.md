# CHANGELOG

## [Sin versionar]

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
