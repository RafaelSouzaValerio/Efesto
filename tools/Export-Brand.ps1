# Renders the original SVG geometry to a PNG preview and a multi-resolution Windows icon.
# Run with Windows PowerShell in STA mode; no external image tooling is required.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
$assets = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\src\Efesto\Assets'))
[xml]$svg = Get-Content -LiteralPath (Join-Path $assets 'efesto.svg') -Raw
$frames = @()
foreach ($size in @(16, 24, 32, 48, 64, 128, 256, 512)) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $drawing = $visual.RenderOpen()
    $drawing.PushTransform((New-Object System.Windows.Media.ScaleTransform ($size / 256.0), ($size / 256.0)))
    foreach ($path in $svg.SelectNodes('//*[local-name()="path"]')) {
        $brush = [System.Windows.Media.BrushConverter]::new().ConvertFromInvariantString($path.fill)
        $geometry = [System.Windows.Media.Geometry]::Parse($path.d)
        $drawing.DrawGeometry($brush, $null, $geometry)
    }
    $drawing.Pop()
    $drawing.Close()
    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $memory = New-Object IO.MemoryStream
    $encoder.Save($memory)
    $bytes = $memory.ToArray()
    $memory.Dispose()
    if ($size -eq 512) { [IO.File]::WriteAllBytes((Join-Path $assets 'efesto.png'), $bytes) }
    else { $frames += [pscustomobject]@{ Size = $size; Bytes = $bytes } }
}
$stream = [IO.File]::Create((Join-Path $assets 'efesto.ico'))
$writer = New-Object IO.BinaryWriter $stream
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
} finally { $writer.Dispose(); $stream.Dispose() }
Write-Output "Logo exportado para $assets"
