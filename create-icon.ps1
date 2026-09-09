param(
    [string]$SourcePath = (Join-Path $PSScriptRoot 'Logo.png'),
    [string]$DestinationPath = (Join-Path $PSScriptRoot 'ForHonorQuickActions\Logo.ico')
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $SourcePath)) {
    throw "Logo image not found: $SourcePath"
}

Add-Type -AssemblyName System.Drawing
$source = [System.Drawing.Image]::FromFile($SourcePath)
$pngs = @()
try {
    foreach ($size in 16, 32, 48, 64, 128, 256) {
        $bitmap = New-Object System.Drawing.Bitmap $size, $size
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.DrawImage($source, 0, 0, $size, $size)
            $memory = New-Object System.IO.MemoryStream
            try {
                $bitmap.Save($memory, [System.Drawing.Imaging.ImageFormat]::Png)
                $pngs += ,@($size, $memory.ToArray())
            }
            finally { $memory.Dispose() }
        }
        finally {
            $graphics.Dispose()
            $bitmap.Dispose()
        }
    }
}
finally { $source.Dispose() }

$folder = Split-Path -Parent $DestinationPath
New-Item -ItemType Directory -Force -Path $folder | Out-Null
$file = [System.IO.File]::Open($DestinationPath, [System.IO.FileMode]::Create)
$writer = New-Object System.IO.BinaryWriter $file
try {
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$pngs.Count)
    $offset = 6 + (16 * $pngs.Count)
    foreach ($entry in $pngs) {
        $size = $entry[0]
        $bytes = $entry[1]
        $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
        $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$bytes.Length)
        $writer.Write([UInt32]$offset)
        $offset += $bytes.Length
    }
    foreach ($entry in $pngs) { $writer.Write($entry[1]) }
}
finally {
    $writer.Dispose()
    $file.Dispose()
}
