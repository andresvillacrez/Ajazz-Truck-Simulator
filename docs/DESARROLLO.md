# Desarrollo

## Backend (`backend.cs` → `backend.exe`)

C# sin dependencias (solo .NET Framework + `System.Drawing`). Compila con el
`csc` del sistema:

```bat
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /optimize /target:exe ^
  /out:plugins\com.hsgaming.scsmon.sdPlugin\backend.exe ^
  plugins\com.hsgaming.scsmon.sdPlugin\backend.cs /reference:System.Drawing.dll
```

- Fuente de datos: MMF `Local\SCSTelemetry` (offsets REV12 documentados en
  `backend.cs` y en `HISTORIAL.md`).
- `INPUT` de `SendInput` en layout explicito de **40 bytes** (x64).
- Diagnostico: `backend.exe -Test` (PNGs a `%TEMP%\scsmon-test`),
  `backend.exe -WatchHigh` (muestreo de largas 60 s).

## DLL de telemetria (terceros)

No se versiona. Descarga `scs-telemetry.dll` (RenCloud, rama v1.12.x):

- Origen: <https://github.com/RenCloud/scs-telemetry/releases>
- Colocala en `dll\` antes de compilar el instalador. El instalador la copia
  a `<juego>\bin\win_x64\plugins\` de ATS/ETS2.

## Instalador (`installer/`)

Inno Setup 6 (`winget install JRSoftware.InnoSetup`). Antes de compilar:

1. Compila `backend.exe` (arriba).
2. Coloca `scs-telemetry.dll` en `dll\`.
3. Compila:

```bat
"%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" installer\truckersim-ajazz.iss
```

Sale en `installer/Output/`. **No probar la instalacion con el juego
abierto** (el instalador cierra StreamDock; el backend en uso no se puede
sobrescribir).

## Imagenes de docs (`docs/img/`)

Se generan con el juego abierto:

```bat
backend.exe -Test
copy %TEMP%\scsmon-test\*.png docs\img\tmp\
```

Elige las representativas y renombralas (`tile-velocidad.png`, ...). Las
fotos del dispositivo fisico las aporta el autor (no se generan por codigo).

## Convenciones

- UUIDs de acciones estables: no renombrar (romperia perfiles de usuarios).
- Version en `manifest.json` + `CHANGELOG.md` + `AppVersion` del `.iss` a la
  vez.
- Todo texto de usuario en espanol (Property Inspector incluido).
