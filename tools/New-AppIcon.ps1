<#
.SYNOPSIS
  Builds the Otear app icon from the artwork in art/otear-icon.jpg (a rounded square on a white background):
  cuts the rounded square out with anti-aliased transparent corners and writes
  src/Otear/Assets/Otear.ico (16-256 px, PNG-compressed entries) plus Logo.png (256 px).
.PARAMETER Source
  Square artwork whose rounded square fills the image edge to edge.
.PARAMETER CornerRadius
  Corner radius of the artwork's rounded square, as a fraction of its width (measured: 225 px of 2000).
#>
[CmdletBinding()]
param(
    [string]$Source,
    [double]$CornerRadius = 0.1125
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
if (-not $Source) { $Source = Join-Path $root 'art/otear-icon.jpg' }
$assets = Join-Path $root 'src/Otear/Assets'
New-Item -ItemType Directory -Force $assets | Out-Null

function New-RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Resize([System.Drawing.Image]$img, [int]$size) {
    $bmp = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $g.CompositingQuality = 'HighQuality'
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $attr = [System.Drawing.Imaging.ImageAttributes]::new()
    $attr.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY) # no dark/white seams at the borders
    $g.DrawImage($img, [System.Drawing.Rectangle]::new(0, 0, $size, $size), 0, 0, $img.Width, $img.Height, 'Pixel', $attr)
    $g.Dispose()
    return $bmp
}

# 1) Master: 1024 px, rounded square cut out with transparent, anti-aliased corners.
$art = [System.Drawing.Bitmap]::new($Source)
if ($art.Width -ne $art.Height) { throw "Artwork must be square (got $($art.Width)x$($art.Height))" }
$masterSize = 1024
$scaled = Resize $art $masterSize
$art.Dispose()

$master = [System.Drawing.Bitmap]::new($masterSize, $masterSize, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($master)
$g.SmoothingMode = 'AntiAlias'
$g.PixelOffsetMode = 'HighQuality'
$g.Clear([System.Drawing.Color]::Transparent)
# Inset by ~1 px so no trace of the white background survives along the curved edge.
$inset = $masterSize * 0.001
$shape = New-RoundedRect $inset $inset ($masterSize - 2 * $inset) ($masterSize - 2 * $inset) ($masterSize * $CornerRadius)
$brush = [System.Drawing.TextureBrush]::new($scaled)
$g.FillPath($brush, $shape)
$g.Dispose()
$scaled.Dispose()

# 2) Icon sizes (downscaled in halving steps for crisp small sizes), PNG-compressed into one .ico.
function Downscale([System.Drawing.Bitmap]$src, [int]$size) {
    $cur = $src
    while ($cur.Width / 2 -ge $size) {
        $next = Resize $cur ([int]($cur.Width / 2))
        if ($cur -ne $src) { $cur.Dispose() }
        $cur = $next
    }
    $final = Resize $cur $size
    if ($cur -ne $src) { $cur.Dispose() }
    return $final
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
$pngs = foreach ($sz in $sizes) {
    $bmp = Downscale $master $sz
    $ms = [IO.MemoryStream]::new()
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($sz -eq 256) { $bmp.Save((Join-Path $assets 'Logo.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
    , $ms.ToArray()
}
$master.Dispose()

# ICO container: ICONDIR + ICONDIRENTRY[] + PNG payloads
$out = [IO.MemoryStream]::new()
$w = [IO.BinaryWriter]::new($out)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $data = $pngs[$i]
    $w.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))
    $w.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))
    $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$data.Length); $w.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($data in $pngs) { $w.Write($data) }
$w.Flush()
[IO.File]::WriteAllBytes((Join-Path $assets 'Otear.ico'), $out.ToArray())
Write-Host "Wrote $(Join-Path $assets 'Otear.ico') ($($sizes -join ', ') px) and Logo.png from $Source"
