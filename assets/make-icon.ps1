# Regenerates AppDispatcher.ico. Coordinates are on a 512 px square.
# Pass -PreviewDir to also write each size as a PNG.
param([string]$PreviewDir)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$bolt = @(158,64), @(286,64), @(226,200), @(302,200), @(106,456), @(162,272), @(78,272)
$stripes = @(
    @{ Color = '#FF6F43'; Points = @(190,255), @(560,20),  @(560,170) },
    @{ Color = '#FFAE2B'; Points = @(190,255), @(560,170), @(560,320) },
    @{ Color = '#FFD391'; Points = @(190,255), @(560,320), @(560,470) }
)

function Points($pairs, [float]$dx = 0, [float]$dy = 0) {
    [System.Drawing.PointF[]]($pairs | ForEach-Object { New-Object System.Drawing.PointF ($_[0] + $dx), ($_[1] + $dy) })
}

function Fill($g, $color, $points) {
    $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml($color))
    $g.FillPolygon($brush, $points)
    $brush.Dispose()
}

# Drawn large and scaled down, because a GDI+ clip region has hard edges.
function Render([int]$size) {
    $scale = if ($size -ge 256) { 4 } else { 8 }
    $big = $size * $scale
    $hi = New-Object System.Drawing.Bitmap $big, $big, ([System.Drawing.Imaging.PixelFormat]::Format32bppPArgb)
    $g = [System.Drawing.Graphics]::FromImage($hi)
    $g.SmoothingMode = 'AntiAlias'
    $g.ScaleTransform($big / 512, $big / 512)

    $diameter = 192
    $corners = New-Object System.Drawing.Drawing2D.GraphicsPath
    $corners.AddArc(0, 0, $diameter, $diameter, 180, 90)
    $corners.AddArc(512 - $diameter, 0, $diameter, $diameter, 270, 90)
    $corners.AddArc(512 - $diameter, 512 - $diameter, $diameter, $diameter, 0, 90)
    $corners.AddArc(0, 512 - $diameter, $diameter, $diameter, 90, 90)
    $corners.CloseFigure()
    $g.SetClip($corners)

    $g.Clear([System.Drawing.Color]::Transparent)
    $ground = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#07190F'))
    $g.FillPath($ground, $corners)
    $ground.Dispose()
    foreach ($s in $stripes) { Fill $g $s.Color (Points $s.Points) }
    Fill $g '#000000' (Points $bolt -7 9)
    Fill $g '#FFEBC8' (Points $bolt)
    $g.Dispose()
    $corners.Dispose()

    $out = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $d = [System.Drawing.Graphics]::FromImage($out)
    $d.InterpolationMode = 'HighQualityBilinear'
    $d.PixelOffsetMode = 'HighQuality'
    $d.CompositingMode = 'SourceCopy'
    $d.DrawImage($hi, 0, 0, $size, $size)
    $d.Dispose()
    $hi.Dispose()
    $out
}

function PngBytes($bitmap) {
    $ms = New-Object System.IO.MemoryStream
    $bitmap.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $ms.ToArray()
}

# The classic icon frame: a bottom-up BGRA bitmap followed by a 1-bit transparency mask.
function DibBytes($bitmap) {
    $size = $bitmap.Width
    $rect = New-Object System.Drawing.Rectangle 0, 0, $size, $size
    $data = $bitmap.LockBits($rect, 'ReadOnly', [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $pixels = New-Object byte[] ($size * $size * 4)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $bitmap.UnlockBits($data)

    $maskStride = [int][math]::Floor(($size + 31) / 32) * 4
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ms
    $w.Write([uint32]40); $w.Write([int32]$size); $w.Write([int32]($size * 2))
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]0); $w.Write([uint32]0); $w.Write([int32]0); $w.Write([int32]0); $w.Write([uint32]0); $w.Write([uint32]0)
    for ($y = $size - 1; $y -ge 0; $y--) { $w.Write($pixels, $y * $size * 4, $size * 4) }
    for ($y = $size - 1; $y -ge 0; $y--) {
        $row = New-Object byte[] $maskStride
        for ($x = 0; $x -lt $size; $x++) {
            if ($pixels[($y * $size + $x) * 4 + 3] -eq 0) { $row[$x -shr 3] = $row[$x -shr 3] -bor (0x80 -shr ($x -band 7)) }
        }
        $w.Write($row)
    }
    $w.Flush()
    $ms.ToArray()
}

$frames = foreach ($size in $sizes) {
    $bitmap = Render $size
    if ($PreviewDir) {
        [void](New-Item -ItemType Directory -Force $PreviewDir)
        $bitmap.Save((Join-Path $PreviewDir "icon-$size.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    # Only the largest frame is PNG, which is the form older icon readers expect.
    $bytes = if ($size -ge 256) { PngBytes $bitmap } else { DibBytes $bitmap }
    $bitmap.Dispose()
    [pscustomobject]@{ Size = $size; Bytes = [byte[]]$bytes }
}

$ms = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $ms
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $dimension = if ($f.Size -ge 256) { 0 } else { $f.Size }
    $w.Write([byte]$dimension); $w.Write([byte]$dimension); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$f.Bytes.Length); $w.Write([uint32]$offset)
    $offset += $f.Bytes.Length
}
foreach ($f in $frames) { $w.Write($f.Bytes) }
$w.Flush()
$target = Join-Path $PSScriptRoot 'AppDispatcher.ico'
[System.IO.File]::WriteAllBytes($target, $ms.ToArray())
Write-Host "Wrote $target ($($ms.Length) bytes)"
