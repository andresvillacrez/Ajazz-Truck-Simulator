param([switch]$Test)
# HS SCS Monitor: telemetria ATS/ETS2 (RenCloud scs-telemetry.dll, MMF Local\SCSTelemetry)
# Offsets segun scs-telemetry-common.hpp (PLUGIN_REVID 12). Sin PDH.

$logFile = Join-Path (Split-Path -Parent $PSCommandPath) "backend.log"
function Log($m) { try { if ((Get-Item $logFile -ErrorAction SilentlyContinue).Length -gt 200KB) { Remove-Item $logFile -Force -ErrorAction SilentlyContinue }; Add-Content $logFile ("{0:HH:mm:ss.fff} [{1}] {2}" -f (Get-Date), $PID, $m) -ErrorAction SilentlyContinue } catch { } }
if (-not $Test) { Log "=== inicio ===" }

Add-Type -AssemblyName System.Drawing

$O = @{
  sdk = 0; paused = 4; game = 52
  speed = 948; rpm = 952; cruise = 988; fuel = 1000; fuelRange = 1008
  limit = 1068; fuelCap = 704; gear = 504; cons = 1004; rpmMax = 740
  onJob = 4300; planned = 100; routeD = 1060; routeT = 1064
  wEng = 1036; wTra = 1040; wCab = 1044; wCha = 1048; wWhe = 1052
  tCd = 6152; tCha = 6156; tWhe = 6160; tBod = 6164; jCd = 1468; tAtt = 6080
  park = 1566; motor = 1567; blinkL = 1580; blinkR = 1581
  lowB = 1583; highB = 1584; brake = 1586; engine = 1576
  cruiseOn = 1589; parking = 1582; hazard = 1587; fuelWarn = 1570
}

function Open-MMF {
  try {
    $mmf = [IO.MemoryMappedFiles.MemoryMappedFile]::OpenExisting("Local\SCSTelemetry")
    return @{ mmf = $mmf; acc = $mmf.CreateViewAccessor(0, 32768, "Read") }
  } catch { return $null }
}
function RF($a, $o) { try { $a.ReadSingle($o) } catch { $null } }
function MRI($a, $o) { try { $a.ReadInt32($o) } catch { $null } }
function RU($a, $o) { try { $a.ReadUInt32($o) } catch { $null } }
function RB($a, $o) { try { $a.ReadByte($o) } catch { $null } }

function Get-Snap($h) {
  if (-not $h) { return @{ active = $false } }
  $a = $h.acc
  $sdk = RB $a $O.sdk
  if (-not $sdk) { return @{ active = $false } }
  $game = RU $a $O.game
  $tw = @((RF $a $O.wEng), (RF $a $O.wTra), (RF $a $O.wCab), (RF $a $O.wCha), (RF $a $O.wWhe)) | Where-Object { $null -ne $_ }
  $td = if ($tw.Count) { ($tw | Measure-Object -Maximum).Maximum * 100 } else { $null }
  $att = [bool](RB $a $O.tAtt)
  $rw = @((RF $a $O.tCha), (RF $a $O.tWhe), (RF $a $O.tBod)) | Where-Object { $null -ne $_ }
  $rd = if ($att -and $rw.Count) { ($rw | Measure-Object -Maximum).Maximum * 100 } else { $null }
  $c1 = RF $a $O.tCd; $c2 = RF $a $O.jCd
  $cd = if ($att -and ($null -ne $c1 -or $null -ne $c2)) { [math]::Max(([double]$c1), ([double]$c2)) * 100 } else { $null }
  return @{
    active = $true; paused = [bool](RB $a $O.paused); ats = ($game -eq 2)
    speed = (RF $a $O.speed); rpm = (RF $a $O.rpm); gear = (MRI $a $O.gear); rpmMax = (RF $a $O.rpmMax)
    fuel = (RF $a $O.fuel); fuelCap = (RF $a $O.fuelCap); range = (RF $a $O.fuelRange)
    cons = (RF $a $O.cons)
    cruise = (RF $a $O.cruise); cruiseOn = [bool](RB $a $O.cruiseOn); limit = (RF $a $O.limit); fuelWarn = [bool](RB $a $O.fuelWarn)
    lowB = [bool](RB $a $O.lowB); highB = [bool](RB $a $O.highB); blinkL = [bool](RB $a $O.blinkL)
    blinkR = [bool](RB $a $O.blinkR); brake = [bool](RB $a $O.brake); park = [bool](RB $a $O.park)
    motor = [bool](RB $a $O.motor); engine = [bool](RB $a $O.engine); parking = [bool](RB $a $O.parking)
    hazard = [bool](RB $a $O.hazard)
    truckDmg = $td; trailerDmg = $rd; cargoDmg = $cd
    onJob = [bool](RB $a $O.onJob); planned = (RU $a $O.planned); routeD = (RF $a $O.routeD); routeT = (RF $a $O.routeT)
  }
}

function Conv-Speed($ms, $ats) {
  if ($null -eq $ms) { return $null }
  if ($ats) { return @{ v = [math]::Round($ms * 2.23694, 0); u = "mph" } }
  return @{ v = [math]::Round($ms * 3.6, 0); u = "km/h" }
}
function Fmt-Gear($g) {
  if ($null -eq $g) { return "--" }
  if ($g -gt 0) { return "$g" }
  if ($g -eq 0) { return "N" }
  return "R"
}

function New-Bmp { New-Object Drawing.Bitmap(72, 72) }
function B64($bmp) {
  $ms = New-Object IO.MemoryStream
  $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
  $b64 = [Convert]::ToBase64String($ms.ToArray())
  $ms.Dispose()
  return "data:image/png;base64,$b64"
}
$themes = @{ green = "Lime"; cyan = "Cyan"; amber = "Orange"; red = "Red"; white = "White"; yellow = "Yellow"; blue = "DodgerBlue"; purple = "Purple"; pink = "DeepPink" }

function Render-Big($top, $label, $colorName, $sub) {
  $bmp = New-Bmp
  $g = [Drawing.Graphics]::FromImage($bmp)
  $g.Clear([Drawing.Color]::Black)
  $g.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAlias
  $col = [Drawing.Color]::FromName($colorName)
  $sf = New-Object Drawing.StringFormat
  $sf.Alignment = [Drawing.StringAlignment]::Center
  $f1 = New-Object Drawing.Font("Arial", 20, [Drawing.FontStyle]::Bold)
  $f2 = New-Object Drawing.Font("Arial", 10, [Drawing.FontStyle]::Bold)
  $f3 = New-Object Drawing.Font("Arial", 9, [Drawing.FontStyle]::Regular)
  $g.DrawString($top, $f1, (New-Object Drawing.SolidBrush($col)), 36, 6, $sf)
  $g.DrawString($label, $f2, [Drawing.Brushes]::White, 36, 34, $sf)
  if ($sub) { $g.DrawString($sub, $f3, [Drawing.Brushes]::Gray, 36, 52, $sf) }
  $out = B64 $bmp
  $g.Dispose(); $bmp.Dispose(); $f1.Dispose(); $f2.Dispose(); $f3.Dispose()
  return $out
}

function Render-Lights($s) {
  $bmp = New-Bmp
  $g = [Drawing.Graphics]::FromImage($bmp)
  $g.Clear([Drawing.Color]::Black)
  $g.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAlias
  $sf = New-Object Drawing.StringFormat
  $sf.Alignment = [Drawing.StringAlignment]::Center
  $f = New-Object Drawing.Font("Arial", 10, [Drawing.FontStyle]::Bold)
  $on = [Drawing.Brushes]::Lime
  $off = New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(70, 70, 70))
  $rows = @(
    @(@("LO", $s.lowB), @("HI", $s.highB), @("PK", $s.parking)),
    @(@("L", $s.blinkL), @("R", $s.blinkR), @("BRK", $s.brake)),
    @(@("MT", $s.motor), @("ENG", $s.engine), @("CRZ", $s.cruiseOn))
  )
  $y = 4
  foreach ($row in $rows) {
    $x = 4
    foreach ($cell in $row) {
      $b = if ($cell[1]) { $on } else { $off }
      $g.DrawString($cell[0], $f, $b, $x + 11, $y, $sf)
      $x += 23
    }
    $y += 22
  }
  $out = B64 $bmp
  $g.Dispose(); $bmp.Dispose(); $f.Dispose(); $off.Dispose()
  return $out
}

function Render-Icon($kind, $on, $colorName) {
  $bmp = New-Bmp
  $g = [Drawing.Graphics]::FromImage($bmp)
  $g.Clear([Drawing.Color]::Black)
  $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $col = if ($on) { [Drawing.Color]::FromName($colorName) } else { [Drawing.Color]::FromArgb(55, 55, 55) }
  $brush = New-Object Drawing.SolidBrush($col)
  $pen = New-Object Drawing.Pen($col, 4)
  $sf = New-Object Drawing.StringFormat
  $sf.Alignment = [Drawing.StringAlignment]::Center
  switch ($kind) {
    "left" {
      $g.FillRectangle($brush, 28, 30, 26, 12)
      $g.FillPolygon($brush, @([Drawing.Point]::new(28, 14), [Drawing.Point]::new(28, 58), [Drawing.Point]::new(6, 36)))
    }
    "right" {
      $g.FillRectangle($brush, 18, 30, 26, 12)
      $g.FillPolygon($brush, @([Drawing.Point]::new(44, 14), [Drawing.Point]::new(44, 58), [Drawing.Point]::new(66, 36)))
    }
    "hazard" {
      $g.DrawPolygon($pen, @([Drawing.Point]::new(36, 8), [Drawing.Point]::new(64, 60), [Drawing.Point]::new(8, 60)))
      $f = New-Object Drawing.Font("Arial", 22, [Drawing.FontStyle]::Bold)
      $g.DrawString("!", $f, $brush, 36, 26, $sf)
      $f.Dispose()
    }
    "low" {
      $g.DrawEllipse($pen, 38, 22, 22, 28)
      foreach ($y in @(26, 36, 46)) { $g.DrawLine($pen, 8, $y, 34, $y) }
    }
    "high" {
      $g.DrawEllipse($pen, 40, 22, 20, 28)
      $g.DrawLine($pen, 6, 22, 34, 30)
      $g.DrawLine($pen, 6, 30, 34, 33)
      $g.DrawLine($pen, 6, 38, 34, 36)
      $g.DrawLine($pen, 6, 46, 34, 39)
      $g.DrawLine($pen, 6, 54, 34, 42)
    }
    "park" {
      $f = New-Object Drawing.Font("Arial", 34, [Drawing.FontStyle]::Bold)
      $g.DrawString("P", $f, $brush, 36, 14, $sf)
      $f.Dispose()
    }
    "brake" {
      $g.FillEllipse($brush, 14, 14, 44, 44)
      $f = New-Object Drawing.Font("Arial", 26, [Drawing.FontStyle]::Bold)
      $tc = if ($on) { [Drawing.Brushes]::Black } else { New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(20, 20, 20)) }
      $g.DrawString("!", $f, $tc, 36, 14, $sf)
      $f.Dispose()
      if ($tc -is [IDisposable] -and $tc -ne [Drawing.Brushes]::Black) { $tc.Dispose() }
    }
  }
  $out = B64 $bmp
  $g.Dispose(); $bmp.Dispose(); $brush.Dispose(); $pen.Dispose()
  return $out
}

function Render-Fuel($s) {
  $bmp = New-Bmp
  $g = [Drawing.Graphics]::FromImage($bmp)
  $g.Clear([Drawing.Color]::Black)
  $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $sf = New-Object Drawing.StringFormat
  $sf.Alignment = [Drawing.StringAlignment]::Center
  $white = New-Object Drawing.SolidBrush([Drawing.Color]::White)
  # icono surtidor convencional (siempre blanco)
  $g.FillRectangle($white, 6, 11, 18, 26)
  $g.FillRectangle($white, 9, 6, 12, 5)
  $g.FillRectangle($white, 4, 37, 22, 4)
  $pen = New-Object Drawing.Pen([Drawing.Color]::White, 2)
  $g.DrawLine($pen, 24, 20, 31, 20)
  $g.DrawLine($pen, 31, 20, 31, 34)
  $g.FillRectangle($white, 28, 32, 6, 8)
  $g.FillRectangle([Drawing.Brushes]::Black, 9, 16, 12, 9)
  # autonomia debajo del icono
  $fN = New-Object Drawing.Font("Arial", 11, [Drawing.FontStyle]::Bold)
  $fU = New-Object Drawing.Font("Arial", 8, [Drawing.FontStyle]::Regular)
  $rk = if ($null -ne $s.range) { "$([math]::Round($s.range,0))" } else { "--" }
  $g.DrawString($rk, $fN, [Drawing.Brushes]::White, 16, 44, $sf)
  $g.DrawString("km", $fU, [Drawing.Brushes]::Gray, 16, 58, $sf)
  # barra vertical de nivel
  $frac = 0
  if ($null -ne $s.fuel -and $null -ne $s.fuelCap -and $s.fuelCap -gt 0) { $frac = $s.fuel / $s.fuelCap }
  if ($frac -lt 0) { $frac = 0 }
  if ($frac -gt 1) { $frac = 1 }
  $warn = $s.fuelWarn -or ($s.active -and $frac -lt 0.12)
  $fillColor = if ($warn) { [Drawing.Color]::Red } else { [Drawing.Color]::Lime }
  $fill = New-Object Drawing.SolidBrush($fillColor)
  $bg = New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(35, 35, 35))
  $bx = 50; $bw = 10; $by = 6; $bh = 60
  $g.FillRectangle($bg, $bx, $by, $bw, $bh)
  $fh = [int]($bh * $frac)
  if ($fh -gt 0) { $g.FillRectangle($fill, $bx, ($by + $bh - $fh), $bw, $fh) }
  $g.DrawRectangle([Drawing.Pens]::Gray, $bx, $by, $bw, $bh)
  $divPen = New-Object Drawing.Pen([Drawing.Color]::White, 1)
  foreach ($q in @(0.25, 0.5, 0.75)) {
    $dy = [int]($by + $bh * (1 - $q))
    $g.DrawLine($divPen, $bx, $dy, ($bx + $bw), $dy)
  }
  $divPen.Dispose()
  $out = B64 $bmp
  $g.Dispose(); $bmp.Dispose(); $white.Dispose(); $pen.Dispose()
  $fN.Dispose(); $fU.Dispose(); $fill.Dispose(); $bg.Dispose()
  return $out
}

$plugDir = Split-Path -Parent $PSCommandPath
$imgs = @{}
foreach ($n in @("left", "right", "hazard", "low", "high", "brake", "park")) {
  foreach ($suf in @("", "-c")) {
    $p = Join-Path $plugDir ("pk-" + $n + $suf + ".png")
    if (Test-Path $p) { $imgs[$n + $suf] = [Drawing.Image]::FromFile($p) }
  }
}
function Render-Pack($name, $on) {
  $bmp = New-Bmp
  $g = [Drawing.Graphics]::FromImage($bmp)
  $g.Clear([Drawing.Color]::Black)
  $key = if ($on) { $name + "-c" } else { $name }
  if ($imgs.ContainsKey($key)) { $g.DrawImage($imgs[$key], 0, 0, 72, 72) }
  $out = B64 $bmp
  $g.Dispose(); $bmp.Dispose()
  return $out
}

function Render-Speed($s, $colorName) {
  $bmp = New-Bmp
  $g = [Drawing.Graphics]::FromImage($bmp)
  $g.Clear([Drawing.Color]::Black)
  $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAlias
  $cx = 36; $cy = 38; $r = 30
  $penW = New-Object Drawing.Pen([Drawing.Color]::White, 3)
  $g.DrawArc($penW, ($cx - $r), ($cy - $r), ($r * 2), ($r * 2), 135, 270)
  for ($a = 135; $a -le 405; $a += 27) {
    $rad = $a * [math]::PI / 180
    $x1 = $cx + [math]::Cos($rad) * $r; $y1 = $cy + [math]::Sin($rad) * $r
    $x2 = $cx + [math]::Cos($rad) * ($r - 5); $y2 = $cy + [math]::Sin($rad) * ($r - 5)
    $g.DrawLine($penW, $x1, $y1, $x2, $y2)
  }
  $col = [Drawing.Color]::FromName($colorName)
  $sf = New-Object Drawing.StringFormat
  $sf.Alignment = [Drawing.StringAlignment]::Center
  $fN = New-Object Drawing.Font("Arial", 24, [Drawing.FontStyle]::Bold)
  $t = "--"
  if ($null -ne $s.speed) { $t = "$([math]::Abs([math]::Round($s.speed * 3.6, 0)))" }
  $g.DrawString($t, $fN, (New-Object Drawing.SolidBrush($col)), 36, 19, $sf)
  $fU = New-Object Drawing.Font("Arial", 9, [Drawing.FontStyle]::Bold)
  $g.DrawString("km/h", $fU, [Drawing.Brushes]::White, 36, 54, $sf)
  $out = B64 $bmp
  $g.Dispose(); $bmp.Dispose(); $penW.Dispose(); $fN.Dispose(); $fU.Dispose()
  return $out
}

function Render-Rpm($s) {
  $t = if ($null -ne $s.rpm) { "$([math]::Round($s.rpm,0))" } else { "--" }
  $zc = "Lime"; $gearTxt = Fmt-Gear $s.gear; $isR = ($gearTxt -eq "R")
  $gearBrush = [Drawing.Brushes]::White
  if ($null -ne $s.rpm -and $null -ne $s.rpmMax -and $s.rpmMax -gt 0) {
    $f = $s.rpm / $s.rpmMax
    if ($f -ge 0.9) { $zc = "Red"; $gearTxt = "CAMBIA"; $gearBrush = [Drawing.Brushes]::Red }
    elseif ($f -ge 0.75) { $zc = "Orange" }
    elseif ($f -ge 0.6) { $zc = "Yellow" }
    else { $zc = "Lime" }
  }
  if ($isR -and $gearTxt -eq "R") { $gearBrush = [Drawing.Brushes]::Red }
  $bmp = New-Bmp
  $g = [Drawing.Graphics]::FromImage($bmp)
  $g.Clear([Drawing.Color]::Black)
  $g.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAlias
  $sf = New-Object Drawing.StringFormat
  $sf.Alignment = [Drawing.StringAlignment]::Center
  $fN = New-Object Drawing.Font("Arial", 18, [Drawing.FontStyle]::Bold)
  $fL = New-Object Drawing.Font("Arial", 9, [Drawing.FontStyle]::Bold)
  $fG = New-Object Drawing.Font("Arial", 15, [Drawing.FontStyle]::Bold)
  $g.DrawString($t, $fN, (New-Object Drawing.SolidBrush([Drawing.Color]::FromName($zc))), 36, 2, $sf)
  $g.DrawString("RPM", $fL, [Drawing.Brushes]::White, 36, 28, $sf)
  $g.DrawString($gearTxt, $fG, $gearBrush, 36, 42, $sf)
  $out = B64 $bmp
  $g.Dispose(); $bmp.Dispose(); $fN.Dispose(); $fL.Dispose(); $fG.Dispose()
  return $out
}

function Dmg-Col($p) {
  if ($null -eq $p) { return $null }
  if ($p -le 20) { return "Cyan" }
  if ($p -le 40) { return "Lime" }
  if ($p -le 60) { return "Yellow" }
  if ($p -le 80) { return "Orange" }
  return "Red"
}

function Draw-DmgIcon($g, $kind, $x, $y, $brush) {
  switch ($kind) {
    "truck" {
      $g.FillRectangle($brush, $x, ($y + 6), 9, 7)
      $g.FillRectangle($brush, ($x + 9), ($y + 9), 5, 4)
      $g.FillEllipse($brush, ($x + 2), ($y + 14), 4, 4)
      $g.FillEllipse($brush, ($x + 10), ($y + 14), 4, 4)
    }
    "trailer" {
      $g.FillRectangle($brush, $x, ($y + 3), 16, 9)
      $g.FillEllipse($brush, ($x + 3), ($y + 13), 4, 4)
      $g.FillEllipse($brush, ($x + 10), ($y + 13), 4, 4)
      $pen = New-Object Drawing.Pen($brush, 2)
      $g.DrawLine($pen, ($x + 16), ($y + 8), ($x + 20), ($y + 8))
      $pen.Dispose()
    }
    "cargo" {
      $pen = New-Object Drawing.Pen($brush, 2)
      $g.DrawRectangle($pen, ($x + 2), ($y + 4), 14, 14)
      $g.DrawLine($pen, ($x + 2), ($y + 4), ($x + 16), ($y + 18))
      $g.DrawLine($pen, ($x + 16), ($y + 4), ($x + 2), ($y + 18))
      $pen.Dispose()
    }
  }
}

function Render-Damage($s) {
  $bmp = New-Bmp
  $g = [Drawing.Graphics]::FromImage($bmp)
  $g.Clear([Drawing.Color]::Black)
  $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAlias
  $sf = New-Object Drawing.StringFormat
  $sf.Alignment = [Drawing.StringAlignment]::Center
  $fP = New-Object Drawing.Font("Arial", 15, [Drawing.FontStyle]::Bold)
  $wht = New-Object Drawing.SolidBrush([Drawing.Color]::White)
  $gry = New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(90, 90, 90))
  $rows = @(@("truck", $s.truckDmg), @("trailer", $s.trailerDmg), @("cargo", $s.cargoDmg))
  $y = 2
  foreach ($row in $rows) {
    Draw-DmgIcon $g $row[0] 3 $y $wht
    if ($null -ne $row[1]) {
      $b = New-Object Drawing.SolidBrush([Drawing.Color]::FromName((Dmg-Col $row[1])))
      $g.DrawString(("$([math]::Round($row[1], 0))%"), $fP, $b, 48, ($y + 1), $sf)
      $b.Dispose()
    } else {
      $g.DrawString("--", $fP, $gry, 48, ($y + 1), $sf)
    }
    $y += 23
  }
  $out = B64 $bmp
  $g.Dispose(); $bmp.Dispose(); $fP.Dispose(); $wht.Dispose(); $gry.Dispose()
  return $out
}

function Fmt-ETA($rt) {
  if ($null -eq $rt -or $rt -le 0) { return "--" }
  if ($rt -ge 3600) { return ("{0}:{1:00}" -f [int]($rt / 3600), [int](($rt % 3600) / 60)) }
  if ($rt -ge 90) { return ("{0} min" -f [int]($rt / 60)) }
  return ("{0} s" -f [int]$rt)
}

function Render-Route($s) {
  if (-not $s.onJob -or $null -eq $s.planned -or $null -eq $s.routeD -or $s.planned -le 0) {
    return (Render-Big "--" "SIN VIAJE" "Gray" $null)
  }
  $trav = $s.planned - $s.routeD
  if ($trav -lt 0) { $trav = 0 }
  $eta = Fmt-ETA $s.routeT
  $bmp = New-Bmp
  $g = [Drawing.Graphics]::FromImage($bmp)
  $g.Clear([Drawing.Color]::Black)
  $g.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAlias
  $sf = New-Object Drawing.StringFormat
  $sf.Alignment = [Drawing.StringAlignment]::Center
  $fL = New-Object Drawing.Font("Arial", 8, [Drawing.FontStyle]::Regular)
  $fN = New-Object Drawing.Font("Arial", 15, [Drawing.FontStyle]::Bold)
  $g.DrawString("RECORRIDA", $fL, [Drawing.Brushes]::Gray, 36, 2, $sf)
  $g.DrawString(("$([math]::Round($trav, 0)) km"), $fN, [Drawing.Brushes]::White, 36, 11, $sf)
  $g.DrawString(("FALTAN " + $eta), $fL, [Drawing.Brushes]::Gray, 36, 35, $sf)
  $g.DrawString(("$([math]::Round($s.routeD, 0)) km"), $fN, (New-Object Drawing.SolidBrush([Drawing.Color]::Cyan)), 36, 44, $sf)
  $out = B64 $bmp
  $g.Dispose(); $bmp.Dispose(); $fL.Dispose(); $fN.Dispose()
  return $out
}

function Render-Key($metric, $s, $colorName) {
  if (-not $s.active) { return (Render-Big "--" "SCS" $colorName "sin juego") }
  switch ($metric) {
    "speed" { return (Render-Speed $s $colorName) }
    "gear" { return (Render-Big (Fmt-Gear $s.gear) "MARCHA" $colorName $null) }
    "rpm" { return (Render-Rpm $s) }
    "fuel" { return (Render-Fuel $s) }
    "range" {
      $t = if ($null -ne $s.range) { "$([math]::Round($s.range,0))" } else { "--" }
      return (Render-Big $t "AUTONOMIA" $colorName "km")
    }
    "cons" {
      $t = if ($null -ne $s.cons) { "$([math]::Round($s.cons * 100, 1))" } else { "--" }
      return (Render-Big $t "L/100KM" $colorName "consumo")
    }
    "cruise" {
      if ($s.cruiseOn -and $null -ne $s.cruise -and $s.cruise -gt 0.5) {
        $c = Conv-Speed $s.cruise $s.ats
        return (Render-Big "$($c.v)" ("CRUCERO " + $c.u) $colorName $null)
      }
      return (Render-Big "OFF" "CRUCERO" $colorName $null)
    }
    "limit" {
      $c = Conv-Speed $s.limit $s.ats
      $t = if ($c -and $s.limit -gt 0.5) { "$($c.v)" } else { "--" }
      $u = if ($c) { "LIM " + $c.u } else { "LIMITE" }
      return (Render-Big $t $u $colorName $null)
    }
    "lowB" { return (Render-Pack "low" $s.lowB) }
    "highB" { return (Render-Pack "high" $s.highB) }
    "blinkL" { return (Render-Pack "left" $s.blinkL) }
    "blinkR" { return (Render-Pack "right" $s.blinkR) }
    "hazard" { return (Render-Pack "hazard" $s.hazard) }
    "brake" { return (Render-Pack "brake" $s.brake) }
    "park" { return (Render-Pack "park" $s.parking) }
    "lights" { return (Render-Lights $s) }
    "damage" { return (Render-Damage $s) }
    "route" { return (Render-Route $s) }
    "auto" {
      if ($s.hazard) { return (Render-Pack "hazard" $true) }
      if ($s.blinkL) { return (Render-Pack "left" $true) }
      if ($s.blinkR) { return (Render-Pack "right" $true) }
      if ($s.brake) { return (Render-Pack "brake" $true) }
      if ($s.highB) { return (Render-Pack "high" $true) }
      if ($s.lowB) { return (Render-Pack "low" $true) }
      if ($s.parking -or $s.park) { return (Render-Pack "park" $true) }
      return (Render-Pack "low" $false)
    }
    "turn" {
      if ($s.hazard) { return (Render-Pack "hazard" $true) }
      if ($s.blinkL) { return (Render-Pack "left" $true) }
      if ($s.blinkR) { return (Render-Pack "right" $true) }
      return (Render-Pack "hazard" $false)
    }
    "beams" {
      if ($s.highB) { return (Render-Pack "high" $true) }
      if ($s.lowB) { return (Render-Pack "low" $true) }
      if ($s.parking) { return (Render-Pack "park" $true) }
      return (Render-Pack "low" $false)
    }
    default { return (Render-Big "--" "SCS" $colorName $null) }
  }
}

if ($Test) {
  $h = Open-MMF
  $s = Get-Snap $h
  if (-not $s.active) { "SIN JUEGO: abre ATS/ETS2 con scs-telemetry.dll y repite el test"; return }
  $c = Conv-Speed $s.speed $s.ats
  "Juego: $(if ($s.ats) {'ATS'} else {'ETS2'}) | Pausado: $($s.paused)"
  "Velocidad: $($c.v) $($c.u) | Marcha: $(Fmt-Gear $s.gear) | RPM: $([math]::Round($s.rpm,0)) (max $([math]::Round($s.rpmMax,0)), zona $(if ($s.rpmMax -gt 0) { [math]::Round($s.rpm / $s.rpmMax * 100, 0).ToString() + '%' } else { '?' }))"
  "Fuel: $([math]::Round($s.fuel,1)) L | Rango: $([math]::Round($s.range,0)) km | Consumo: $([math]::Round($s.cons * 100,1)) L/100km | Warn: $($s.fuelWarn) | Frac: $([math]::Round($s.fuel / $s.fuelCap * 100, 0))%"
  $cr = Conv-Speed $s.cruise $s.ats
  "Crucero: $(if ($s.cruiseOn) {"$($cr.v) $($cr.u)"} else {'OFF'}) | Limite: $((Conv-Speed $s.limit $s.ats).v)"
  "Danos: camion $([math]::Round($s.truckDmg,1))% | trailer $(if ($null -ne $s.trailerDmg) { [math]::Round($s.trailerDmg,1).ToString() + '%' } else { 'sin trailer' }) | carga $(if ($null -ne $s.cargoDmg) { [math]::Round($s.cargoDmg,1).ToString() + '%' } else { 's/carga' })"
  "Luces: LO=$($s.lowB) HI=$($s.highB) L=$($s.blinkL) R=$($s.blinkR) HAZ=$($s.hazard) BRK=$($s.brake) PK=$($s.parking)"
  $t = [IO.Path]::Combine([IO.Path]::GetTempPath(), "scsmon-test")
  New-Item -ItemType Directory $t -Force | Out-Null
  foreach ($m in @("speed", "gear", "rpm", "fuel", "range", "cons", "cruise", "limit", "blinkL", "blinkR", "hazard", "lowB", "highB", "brake", "park", "lights", "auto", "turn", "beams", "damage", "route")) {
    $img = Render-Key $m $s "Lime"
    [IO.File]::WriteAllBytes("$t\$m.png", [Convert]::FromBase64String($img.Substring(22)))
  }
  "PNGs en $t"
  if ($h) { $h.acc.Dispose(); $h.mmf.Dispose() }
  return
}

# --- Host: -port N -pluginUUID X -registerEvent Y -info {...} ---
$port = $null; $uuid = $null; $regEvent = "registerPlugin"
for ($i = 0; $i -lt $args.Count; $i++) {
  switch ($args[$i]) {
    "-port" { $port = $args[++$i] }
    "-pluginUUID" { $uuid = $args[++$i] }
    "-registerEvent" { $regEvent = $args[++$i] }
  }
}
if (-not $port -or -not $uuid) { exit 1 }

$ws = New-Object Net.WebSockets.ClientWebSocket
$ws.ConnectAsync("ws://127.0.0.1:$port", [Threading.CancellationToken]::None).Wait()
function Send-Msg($obj) {
  $bytes = [Text.Encoding]::UTF8.GetBytes(($obj | ConvertTo-Json -Compress -Depth 6))
  try {
    $ws.SendAsync([ArraySegment[byte]]$bytes, [Net.WebSockets.WebSocketMessageType]::Text, $true, [Threading.CancellationToken]::None).Wait()
  } catch { Log ("MUERTE-envio: " + $_.Exception.Message); exit 0 }
}
Send-Msg @{ event = $regEvent; uuid = $uuid }
if (-not $Test) { Log "registrado" }

$h = Open-MMF
$contexts = @{}
$buf = New-Object byte[] 65536
$ct = [Threading.CancellationToken]::None
while ($ws.State -eq [Net.WebSockets.WebSocketState]::Open) {
  $t0 = Get-Date
  $deadline = (Get-Date).AddMilliseconds(50)
  while ((Get-Date) -lt $deadline) {
    $ms2 = New-Object IO.MemoryStream
    $gotFrame = $false
    $closed = $false
    while ($true) {
      $seg = New-Object ArraySegment[byte](, $buf)
      $callCts = New-Object Threading.CancellationTokenSource
      $task = $null
      try { $task = $ws.ReceiveAsync($seg, $callCts.Token) } catch { Log ("MUERTE-rx2: " + $_.Exception.Message); $callCts.Dispose(); $closed = $true; break }
      $left = ($deadline - (Get-Date)).TotalMilliseconds
      if ($left -le 0 -or -not $task.AsyncWaitHandle.WaitOne([math]::Max(1, [int]$left))) {
        try { $callCts.Cancel() } catch { }
        try { $task.GetAwaiter().GetResult() | Out-Null } catch { }
        $callCts.Dispose()
        break
      }
      $callCts.Dispose()
      try { $r = $task.GetAwaiter().GetResult() } catch { Log ("MUERTE-rx: " + $_.Exception.Message); $closed = $true; break }
      $gotFrame = $true
      Log ("rx count=" + $r.Count + " fin=" + $r.EndOfMessage + " type=" + $r.MessageType)
      if ($r.MessageType -eq [Net.WebSockets.WebSocketMessageType]::Close) { $closed = $true; break }
      if ($r.Count -gt 0) { $ms2.Write($buf, 0, $r.Count) }
      if ($r.EndOfMessage) { break }
    }
    if ($closed) { $ms2.Dispose(); Log "MUERTE-socket-cerrado"; exit 0 }
    if (-not $gotFrame -or $ms2.Length -eq 0) { $ms2.Dispose(); break }
    try { $msg = [Text.Encoding]::UTF8.GetString($ms2.ToArray()) | ConvertFrom-Json } catch { $ms2.Dispose(); continue }
    $ms2.Dispose()
    Log ("ev " + $msg.event)
    try {
      switch ($msg.event) {
        "willAppear" {
          $base = "$($msg.action)".Split(".")[-1]
          if (-not $base) { $base = "monitor" }
          $contexts[$msg.context] = @{ settings = $msg.payload.settings; visible = $true; base = $base }
        }
        "willDisappear" { if ($contexts.ContainsKey($msg.context)) { $contexts[$msg.context].visible = $false } }
        { $_ -in "didReceiveSettings", "didReceiveGlobalSettings" } {
          if ($msg.context -and $contexts.ContainsKey($msg.context)) { $contexts[$msg.context].settings = $msg.payload.settings }
        }
      }
    } catch { }
  }
  if ($ws.State -ne [Net.WebSockets.WebSocketState]::Open) { Log ("MUERTE-estado: " + $ws.State); exit 0 }
  if (-not $h) { $h = Open-MMF }
  try { $s = Get-Snap $h } catch { $s = @{ active = $false } }
  if (-not $s.active -and $h) { $h.acc.Dispose(); $h.mmf.Dispose(); $h = $null }
  foreach ($ctx in @($contexts.Keys)) {
    if (-not $contexts[$ctx].visible) { continue }
    $st = $contexts[$ctx].settings
    $base = $contexts[$ctx].base
    $metric = if ($base -and $base -ne "monitor") { $base } elseif ($st.metric) { $st.metric } else { "speed" }
    $theme = if ($st.theme -and $themes.ContainsKey($st.theme)) { $st.theme } else { "green" }
    try { $img = Render-Key $metric $s $themes[$theme] } catch { Log ("render: " + $_.Exception.Message); continue }
    if ($contexts[$ctx].last -ne $img) {
      Send-Msg @{ event = "setImage"; context = $ctx; payload = @{ image = $img; target = 0 } }
      $contexts[$ctx].last = $img
    }
    if ($contexts[$ctx].titled) {
      Send-Msg @{ event = "setTitle"; context = $ctx; payload = @{ title = ""; target = 0 } }
      $contexts[$ctx].titled = $true
    }
  }
  $ints = @($contexts.Values | Where-Object { $_.visible } | ForEach-Object { if ($_.settings.interval) { [double]$_.settings.interval } else { 0.15 } })
  $wait = if ($ints.Count) { ($ints | Measure-Object -Minimum).Minimum } else { 2 }
  if ($wait -lt 0.1) { $wait = 0.1 }
  $rest = [int]($wait * 1000 - ((Get-Date) - $t0).TotalMilliseconds)
  if ($rest -gt 0) { Start-Sleep -Milliseconds $rest }
}
