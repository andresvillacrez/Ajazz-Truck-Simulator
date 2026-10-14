param([switch]$TestOnce)
# Watcher auto-gaming SCS: ATS (amtrucks) + ETS2 (eurotrucks2)
# Sin admin. Detecta juego -> cierra fondo. Al salir -> restaura.
$procs = @("amtrucks", "eurotrucks2")
$oneDriveExe = "$env:LOCALAPPDATA\Microsoft\OneDrive\OneDrive.exe"
$gaming = $false
$wasOneDrive = $false

function Test-GameRunning {
  foreach ($p in $procs) {
    if (Get-Process -Name $p -ErrorAction SilentlyContinue) { return $true }
  }
  return $false
}
function Enter-Gaming {
  $script:wasOneDrive = [bool](Get-Process -Name OneDrive -ErrorAction SilentlyContinue)
  Stop-Process -Name OneDrive -Force -ErrorAction SilentlyContinue
  $script:gaming = $true
}
function Exit-Gaming {
  if ($script:wasOneDrive -and (Test-Path $oneDriveExe)) { Start-Process $oneDriveExe -ArgumentList "/background" }
  $script:gaming = $false
  $script:wasOneDrive = $false
}

if ($TestOnce) {
  # Modo prueba: una sola iteracion, sin matar nada
  if (Test-GameRunning) { Write-Output "TEST: juego detectado" } else { Write-Output "TEST: sin juego, watcher OK" }
  return
}

while ($true) {
  if ((Test-GameRunning) -and (-not $gaming)) { Enter-Gaming }
  elseif ((-not (Test-GameRunning)) -and ($gaming)) { Exit-Gaming }
  Start-Sleep -Seconds 5
}
