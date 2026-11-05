param(
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Resolve-Path (Join-Path $here "..\..")
$plugin = Join-Path $root "plugins\com.hsgaming.scsmon.sdPlugin"
$source = Join-Path $here "src\backend.cs"
$output = Join-Path $plugin "backend.exe"
$iss = Join-Path $root "installer\truckersim-ajazz.iss"

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path -LiteralPath $csc)) {
    throw "No se encontró csc.exe de .NET Framework 4.x."
}
if (-not (Test-Path -LiteralPath $source)) {
    throw "No se encontró backend.cs en $source"
}

Write-Host "Compilando backend Windows..." -ForegroundColor Cyan
& $csc /nologo /optimize /target:exe /out:$output $source /reference:System.Drawing.dll
if ($LASTEXITCODE -ne 0) {
    throw "Falló la compilación de backend.exe."
}
Write-Host "OK: $output" -ForegroundColor Green

if (-not $SkipInstaller) {
    $isccCandidates = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    )
    $iscc = $isccCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

    if ($iscc) {
        if (-not (Test-Path -LiteralPath (Join-Path $root "dll\scs-telemetry.dll"))) {
            Write-Host "AVISO: falta dll\scs-telemetry.dll; el instalador fallará." -ForegroundColor Yellow
        }
        Write-Host "Generando instalador con Inno Setup..." -ForegroundColor Cyan
        & $iscc $iss
        if ($LASTEXITCODE -ne 0) {
            throw "Falló Inno Setup."
        }
        Write-Host "Instalador en installer\Output\" -ForegroundColor Green
    } else {
        Write-Host "Inno Setup 6 no encontrado; se omite el instalador." -ForegroundColor Yellow
    }
}
