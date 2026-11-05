# Ports de Trucker Simulator

Esta carpeta separa el proyecto por plataforma de destino. La lógica de
telemetría, las acciones y los recursos visuales siguen siendo compartidos;
cada port aporta su backend, su manifiesto y su empaquetado.

```text
ports/
├─ windows/          Backend C# + instalador Inno Setup
├─ macos/            Backend Swift + ZIP/PKG
└─ linux-opendeck/   Integración con OpenDeck (puente Wine/Proton)
```

## Matriz rápida

| Port | Estado | Backend | Entrada | Empaquetado |
|---|---|---|---|---|
| Windows | Estable 1.2 | `backend.cs` → `backend.exe` | StreamDock / Ajazz | Inno Setup |
| macOS | Beta 1.0 | `backend.swift` → `backend` | StreamDock | ZIP / PKG |
| Linux + OpenDeck | Experimental | Puente Wine sobre `backend.exe` | OpenDeck | `.sdPlugin` + script |

## Recursos compartidos

- `../plugins/com.hsgaming.scsmon.sdPlugin/`: manifiesto base, Property
  Inspector, iconos y PNGs de las 34 acciones.
- `../dll/scs-telemetry.dll`: plugin SCS para Windows.
- `../docs/`: manuales, compatibilidad y licencias.
- `../installer/`: instalador Windows y empaquetado macOS existentes.

Los ports **no duplican** los recursos: los scripts de empaquetado los toman
de la carpeta compartida y generan una carpeta de salida propia.

## Mejoras recomendadas

Ver [`MEJORAS.md`](MEJORAS.md) para la auditoría priorizada.

## Reglas para no romper compatibilidad

1. No cambiar los UUID de las acciones: los perfiles de los usuarios los usan.
2. Mantener la versión sincronizada entre `manifest.json`, `CHANGELOG.md` y el
   instalador de cada port.
3. Los recursos con el mismo nombre (`tile-*.png`, `pk-*.png`) deben ser
   idénticos en todos los ports.
4. Los backends hablan el protocolo WebSocket de StreamDeck/OpenDeck; no
   inventar un protocolo propio por plataforma.
