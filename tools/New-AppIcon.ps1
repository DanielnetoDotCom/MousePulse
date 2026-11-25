<#
.SYNOPSIS
    Generates src/MousePulse/Resources/MousePulse.ico (multi-resolution, PNG-compressed entries).
.DESCRIPTION
    The icon is drawn with System.Drawing so the repository needs no binary design assets.
    Re-run this script only if you want to change the artwork.
#>
param(
    [string]$OutFile = (Join-Path $PSScriptRoot '..\src\MousePulse\Resources\MousePulse.ico')
)

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # Rounded square background with a vertical gradient.
    $pad = [Math]::Max(1.0, $size * 0.04)
    $r = $size * 0.22
    $rect = New-Object System.Drawing.RectangleF $pad, $pad, ($size - 2 * $pad), ($size - 2 * $pad)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $top = [System.Drawing.Color]::FromArgb(255, 37, 99, 235)    # blue
    $bottom = [System.Drawing.Color]::FromArgb(255, 13, 148, 136) # teal
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, $top, $bottom, 90.0
    $g.FillPath($brush, $path)

    # Pulse line.
    $w = [Math]::Max(1.5, $size * 0.085)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $w
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $s = $size
    $pts = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF (0.16 * $s), (0.55 * $s)),
        (New-Object System.Drawing.PointF (0.36 * $s), (0.55 * $s)),
        (New-Object System.Drawing.PointF (0.45 * $s), (0.28 * $s)),
        (New-Object System.Drawing.PointF (0.56 * $s), (0.74 * $s)),
        (New-Object System.Drawing.PointF (0.65 * $s), (0.48 * $s)),
        (New-Object System.Drawing.PointF (0.84 * $s), (0.48 * $s))
    )
    $g.DrawLines($pen, $pts)

    $pen.Dispose(); $brush.Dispose(); $path.Dispose(); $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = foreach ($sz in $sizes) {
    $bmp = New-IconBitmap $sz
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}

$dir = Split-Path -Parent $OutFile
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }

$fs = [System.IO.File]::Create($OutFile)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $data = $images[$i]
    $dim = if ($sz -ge 256) { 0 } else { $sz }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$data.Length); $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($data in $images) { $bw.Write($data) }
$bw.Dispose()
Write-Host "Icon written to $((Resolve-Path $OutFile).Path)"
