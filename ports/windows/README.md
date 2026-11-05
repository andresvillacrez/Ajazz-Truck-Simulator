# Port Windows

Backend nativo C# para StreamDock / Ajazz en Windows 10/11 x64.

## Estado

- Versión estable: **1.3.0**, 34 acciones (27 Keypad + 3 Knob).
- `fuel`, `range` y `route` rediseñados.
- Backend: `src/backend.cs`.
- WebSocket: `Ws.SendRaw` reutiliza cabecera, mascara y buffer de 8 KB; sin
  `MemoryStream` ni copia completa del frame (500x30 KB: 132 ms -> 102 ms).
- `FlashWatch` comparte el visor de telemetria del bucle principal y adapta
  el sondeo: 15 ms activo, 100 ms en pausa, 250 ms sin juego/MMF.
- Instalador: `../../installer/truckersim-ajazz.iss` (Inno Setup 6).
- Telemetría: `../../dll/scs-telemetry.dll` (RenCloud, MIT; no versionada).

## Compilar

Requisitos: .NET Framework 4.x (`csc.exe`) y, para el instalador, Inno Setup 6.

```powershell
.\build-windows.ps1
```

El script:

1. Compila `backend.cs` a `backend.exe` en la carpeta compartida del plugin.
2. Si encuentra `ISCC.exe`, regenera el instalador en
   `../../installer/Output/`.
3. No copia la DLL de telemetría: debe estar en `../../dll/` antes de compilar
   el instalador.

Compilación manual equivalente:

```bat
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /optimize /target:exe ^
  /out:..\..\plugins\com.hsgaming.scsmon.sdPlugin\backend.exe ^
  src\backend.cs /reference:System.Drawing.dll
```

## Probar

```bat
backend.exe -Test        :: PNGs a %TEMP%\scsmon-test
backend.exe -RenderDiag  :: 22 acciones a 72/96/144 px
backend.exe -WatchHigh   :: muestreo de largas
backend.exe -NoInput     :: eventos sin inyectar teclas
backend.exe -WindowsInfo :: perfiles y bindings (ventanillas + encoders)
backend.exe -WindowsFix  :: agrega bindings de ventanillas/encoders
```

## Instalar en desarrollo

Cierra StreamDock y el backend, copia `backend.exe` y `manifest.json` a:

```text
%APPDATA%\HotSpot\StreamDock\plugins\com.hsgaming.scsmon.sdPlugin\
```

## Encoders (AKP03E / AKP05)

Acciones: `cruiseenc`, `retarderenc`, `volumeenc` (`Controllers: ["Knob"]`).
En StreamDock aparecen en **Rueda > Trucker Simulator**.

- Crucero: gira `cruiectrlinc` / `cruiectrldec`; pulso = `C`.
- Retarder: gira `retarderup` / `retarderdown`; pulso = `B` (freno motor).
- Volumen: teclas multimedia de Windows; pulso = mute.
- Si el perfil no tiene tecla para esas acciones, el plugin agrega una libre
  (misma ruta que las ventanillas, backup `.hs-window-backup`, sin pisar
  bindings). Espera unos segundos y reinicia el juego una vez.
- `dialRotate` usa `ticks` y encola una sola tarea en `scsmon-input`;
  `dialDown`/`touchTap` y `keyDown` comparten antirrebote de 250 ms.
- El Property Inspector `encoder.html` permite invertir el giro y ajustar
  las teclas por muesca (1-5).

## Mejoras pendientes

- Separar `backend.cs` en módulos (telemetría, render, protocolo, input).
- Instalación por usuario sin admin para la app; elevar solo la copia de la DLL
  a las carpetas de ATS/ETS2.
- Firma de código del `.exe` y del instalador.
- Rotación de `backend.log`.
- Pruebas de snapshot de los 35 tiles.
- Ajuste fino del tamaño de render por modelo real (hoy heurística por
  `deviceInfo.size`/nombre).

Ver `../MEJORAS.md`.
