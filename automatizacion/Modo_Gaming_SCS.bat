@echo off
chcp 65001 >nul
title Modo Gaming SCS - ATS / ETS2
echo ============================================
echo  MODO GAMING SCS - sin microtirones
echo ============================================
echo.
echo [1] Pausando OneDrive para jugar sin tirones...
taskkill /F /IM OneDrive.exe 2>nul
echo     OneDrive pausado. OBS y Discord intactos para streaming.
echo.
echo Elige juego:
echo   1 = American Truck Simulator
echo   2 = Euro Truck Simulator 2
set /p OPT="Opcion (1/2): "

set STEAMDIR=C:\Program Files (x86)\Steam
set OPTS=-nointro -mm_pool_size 12000 -mm_max_resource_size 32 -mm_max_tmp_buffers_size 1000

tasklist /FI "IMAGENAME eq steam.exe" | find /I "steam.exe" >nul
if errorlevel 1 (
  echo [2] Abriendo Steam...
  start "" "%STEAMDIR%\steam.exe"
  timeout /t 10 /nobreak >nul
)

if "%OPT%"=="2" goto ETS2

:ATS
echo [3] Lanzando ATS en modo optimizado...
start "" /wait "%STEAMDIR%\steamapps\common\American Truck Simulator\bin\win_x64\amtrucks.exe" %OPTS%
goto RESTORE

:ETS2
echo [3] Lanzando ETS2 en modo optimizado...
start "" /wait "%STEAMDIR%\steamapps\common\Euro Truck Simulator 2\bin\win_x64\eurotrucks2.exe" %OPTS%
goto RESTORE

:RESTORE
echo.
echo [4] Restaurando OneDrive...
start "" "%LOCALAPPDATA%\Microsoft\OneDrive\OneDrive.exe" /background
echo     OneDrive restaurado.
echo.
echo Listo. Pulsa una tecla para cerrar.
pause >nul
