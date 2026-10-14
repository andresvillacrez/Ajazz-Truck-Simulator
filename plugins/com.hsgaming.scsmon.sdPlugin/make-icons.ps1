Add-Type -AssemblyName System.Drawing
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$items = @(
    @{ File = 'tile-gear'; Label = 'GEAR'; Mark = 'H'; Color = 'Gold' },
    @{ File = 'tile-rpm'; Label = 'RPM'; Mark = '↗'; Color = 'Orange' },
    @{ File = 'tile-fuel'; Label = 'FUEL'; Mark = '⛽'; Color = 'Lime' },
    @{ File = 'tile-cons'; Label = 'L/100'; Mark = '∿'; Color = 'Cyan' },
    @{ File = 'tile-limit'; Label = 'LIM'; Mark = '50'; Color = 'Red' },
    @{ File = 'tile-temp'; Label = 'MOTOR'; Mark = '°'; Color = 'OrangeRed' },
    @{ File = 'tile-oil'; Label = 'ACEITE'; Mark = '◉'; Color = 'Yellow' },
    @{ File = 'tile-air'; Label = 'AIRE'; Mark = '◌'; Color = 'DeepSkyBlue' },
    @{ File = 'tile-damage'; Label = 'DANOS'; Mark = '!'; Color = 'Red' },
    @{ File = 'tile-route'; Label = 'RUTA'; Mark = '>'; Color = 'Lime' }
)
foreach ($item in $items) {
    $bitmap = New-Object Drawing.Bitmap(72, 72)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAlias
    $graphics.Clear([Drawing.Color]::FromArgb(16, 20, 28))
    $pen = New-Object Drawing.Pen([Drawing.Color]::FromArgb(190, 200, 210), 2)
    $graphics.DrawRectangle($pen, 3, 3, 66, 66)
    $iconSize = if ($item.Mark.Length -gt 1) { 25 } else { 32 }
    $font = New-Object Drawing.Font('Arial', $iconSize, [Drawing.FontStyle]::Bold)
    $format = New-Object Drawing.StringFormat
    $format.Alignment = [Drawing.StringAlignment]::Center
    $format.LineAlignment = [Drawing.StringAlignment]::Center
    $rect = New-Object Drawing.RectangleF(3, 3, 66, 66)
    $brush = New-Object Drawing.SolidBrush([Drawing.Color]::FromName($item.Color))
    $markRect = New-Object Drawing.RectangleF(3, 3, 66, 47)
    $graphics.DrawString($item.Mark, $font, $brush, $markRect, $format)
    $labelFont = New-Object Drawing.Font('Arial', 8, [Drawing.FontStyle]::Bold)
    $graphics.DrawString($item.Label, $labelFont, [Drawing.Brushes]::White, 36, 51, $format)
    $bitmap.Save((Join-Path $dir ($item.File + '.png')), [Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $bitmap.Dispose(); $pen.Dispose(); $font.Dispose(); $labelFont.Dispose(); $brush.Dispose(); $format.Dispose()
}
