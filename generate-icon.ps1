$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# PNG-backed ICO frames preserve alpha and the original Sonar colors.
# Icon.FromHandle(...).Save(...) converts this artwork to a legacy bitmap
# format that loses alpha on some Windows/.NET combinations.
$source = [System.Drawing.Bitmap]::new((Join-Path $PSScriptRoot 'assets\sonar.png'))
$frames = @()
try {
    foreach ($size in @(16, 20, 24, 32, 40, 48, 64, 256)) {
        $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $buffer = [System.IO.MemoryStream]::new()
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.DrawImage($source, [System.Drawing.Rectangle]::new(0, 0, $size, $size))
            $bitmap.Save($buffer, [System.Drawing.Imaging.ImageFormat]::Png)
            $frames += [pscustomobject]@{ Size = $size; Bytes = $buffer.ToArray() }
        }
        finally { $buffer.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
    }
}
finally { $source.Dispose() }

$stream = [System.IO.File]::Create((Join-Path $PSScriptRoot 'assets\sonar.ico'))
$writer = [System.IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
}
finally { $writer.Dispose(); $stream.Dispose() }
