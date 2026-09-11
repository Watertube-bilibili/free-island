param([Parameter(Mandatory = $true)][string] $OutputPath)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$iconSize = 256
$bitmap = New-Object System.Drawing.Bitmap($iconSize, $iconSize, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$canvas = [System.Drawing.Graphics]::FromImage($bitmap)
$canvas.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$canvas.Clear([System.Drawing.Color]::Transparent)

# The same normalized 24-unit brand as AppVisual.Brand: a flat cobalt circle,
# a white 14 x 5 island capsule at (5, 12.5), and a 7 x 3 sky capsule at (10, 6.5).
$scale = [single]($iconSize / 24.0)
$canvas.ScaleTransform($scale, $scale)
$cobalt = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(79, 102, 232))
$canvas.FillEllipse($cobalt, [single]0, [single]0, [single]24, [single]24)
$cobalt.Dispose()

$island = New-Object System.Drawing.Pen([System.Drawing.Color]::White, [single]5)
$island.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$island.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$canvas.DrawLine($island, [single]7.5, [single]15, [single]16.5, [single]15)
$island.Dispose()
$sky = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(185, 205, 255), [single]3)
$sky.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$sky.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$canvas.DrawLine($sky, [single]11.5, [single]8, [single]15.5, [single]8)
$sky.Dispose()
$canvas.Dispose()

$memory = New-Object System.IO.MemoryStream
$bitmap.Save($memory, [System.Drawing.Imaging.ImageFormat]::Png)
$pngBytes = $memory.ToArray()
$memory.Dispose()
$bitmap.Dispose()

$absoluteOutput = [System.IO.Path]::GetFullPath($OutputPath)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($absoluteOutput)) | Out-Null
[System.IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'FreeIsland.png'), $pngBytes)
$stream = [System.IO.File]::Create($absoluteOutput)
$writer = New-Object System.IO.BinaryWriter($stream)
try {
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]1)
    $writer.Write([Byte]0)
    $writer.Write([Byte]0)
    $writer.Write([Byte]0)
    $writer.Write([Byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$pngBytes.Length)
    $writer.Write([UInt32]22)
    $writer.Write($pngBytes)
} finally {
    $writer.Dispose()
}
