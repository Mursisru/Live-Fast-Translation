#Requires -Version 5.1
<#
    Regenerates the colored application logo and the multi-size app.ico.

    Monochrome navigation icons are vector Path elements in XAML: raster PNGs looked
    blurry and distorted at 16 px. Only the full-color logo stays as a bitmap.

    Run:
      powershell -NoProfile -ExecutionPolicy Bypass -File scripts\regenerate_icons.ps1
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Add-Type -AssemblyName System.Drawing

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$srcDir = Join-Path $repo 'src\LiveFastTranslation\Assets\new_assets'
$outDir = Join-Path $repo 'src\LiveFastTranslation\Assets'

function Get-AlphaBounds([System.Drawing.Bitmap]$image) {
    $minX = $image.Width
    $minY = $image.Height
    $maxX = -1
    $maxY = -1
    for ($y = 0; $y -lt $image.Height; $y++) {
        for ($x = 0; $x -lt $image.Width; $x++) {
            if ($image.GetPixel($x, $y).A -gt 8) {
                if ($x -lt $minX) { $minX = $x }
                if ($y -lt $minY) { $minY = $y }
                if ($x -gt $maxX) { $maxX = $x }
                if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }
    if ($maxX -lt 0) { throw 'The source image is fully transparent.' }
    return @($minX, $minY, ($maxX - $minX + 1), ($maxY - $minY + 1))
}

function Export-SquareIcon([string]$sourcePath, [string]$targetPath, [int]$size) {
    $source = [System.Drawing.Bitmap]::FromFile($sourcePath)
    try {
        $bounds = Get-AlphaBounds $source
        $crop = New-Object System.Drawing.Bitmap($bounds[2], $bounds[3], [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $cropGraphics = [System.Drawing.Graphics]::FromImage($crop)
        $cropGraphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $cropGraphics.DrawImage(
            $source,
            (New-Object System.Drawing.Rectangle(0, 0, $bounds[2], $bounds[3])),
            (New-Object System.Drawing.Rectangle($bounds[0], $bounds[1], $bounds[2], $bounds[3])),
            [System.Drawing.GraphicsUnit]::Pixel)
        $cropGraphics.Dispose()

        $canvas = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($canvas)
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality

        # Two pixels on each side keep antialiasing from touching the canvas edge.
        $inner = $size - 4
        $scale = [Math]::Min($inner / $bounds[2], $inner / $bounds[3])
        $width = [int][Math]::Round($bounds[2] * $scale)
        $height = [int][Math]::Round($bounds[3] * $scale)
        $x = [int][Math]::Round(($size - $width) / 2)
        $y = [int][Math]::Round(($size - $height) / 2)
        $graphics.DrawImage($crop, (New-Object System.Drawing.Rectangle($x, $y, $width, $height)))
        $graphics.Dispose()
        $crop.Dispose()

        $canvas.Save($targetPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $canvas.Dispose()
        Write-Output ("PNG {0} ({1}x{1}, art {2}x{3})" -f $targetPath, $size, $width, $height)
    }
    finally {
        $source.Dispose()
    }
}

function Export-Ico([string]$sourcePath, [string]$targetPath, [int[]]$sizes) {
    $source = [System.Drawing.Bitmap]::FromFile($sourcePath)
    $frames = @()
    try {
        foreach ($size in $sizes) {
            $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $graphics.DrawImage($source, (New-Object System.Drawing.Rectangle(0, 0, $size, $size)))
            $graphics.Dispose()

            $stream = New-Object System.IO.MemoryStream
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $bitmap.Dispose()
            $frames += ,@($size, $stream.ToArray())
            $stream.Dispose()
        }
    }
    finally {
        $source.Dispose()
    }

    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $size = $frame[0]
        $data = $frame[1]
        $writer.Write([byte]($(if ($size -ge 256) { 0 } else { $size })))
        $writer.Write([byte]($(if ($size -ge 256) { 0 } else { $size })))
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$data.Length)
        $writer.Write([uint32]$offset)
        $offset += $data.Length
    }
    foreach ($frame in $frames) {
        $writer.Write($frame[1])
    }
    $writer.Flush()
    [System.IO.File]::WriteAllBytes($targetPath, $stream.ToArray())
    $writer.Dispose()
    $stream.Dispose()
    Write-Output ("ICO {0} ({1} frames)" -f $targetPath, $frames.Count)
}

$pngs = @(Get-ChildItem -LiteralPath $srcDir -Filter '*.png')
$app = $pngs | Where-Object { $_.Name -like ([string][char]0x0418 + '*') } | Select-Object -First 1
if (-not $app) {
    throw 'Original application PNG was not found.'
}

Export-SquareIcon $app.FullName (Join-Path $outDir 'app-icon.png') 256
Export-Ico (Join-Path $outDir 'app-icon.png') (Join-Path $outDir 'app.ico') @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
