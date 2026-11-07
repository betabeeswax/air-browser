Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root "assets"
New-Item -ItemType Directory -Force $assets | Out-Null
$bmp = New-Object System.Drawing.Bitmap 256, 256
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)
$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$path.AddArc(8, 8, 52, 52, 180, 90)
$path.AddArc(196, 8, 52, 52, 270, 90)
$path.AddArc(196, 196, 52, 52, 0, 90)
$path.AddArc(8, 196, 52, 52, 90, 90)
$path.CloseFigure()
$brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
  (New-Object System.Drawing.Rectangle 0, 0, 256, 256),
  ([System.Drawing.Color]::FromArgb(52, 151, 255)),
  ([System.Drawing.Color]::FromArgb(12, 57, 132)),
  45
)
$g.FillPath($brush, $path)
$pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, 25)
$pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
$points = [System.Drawing.Point[]]@(
  (New-Object System.Drawing.Point 76, 184),
  (New-Object System.Drawing.Point 128, 66),
  (New-Object System.Drawing.Point 180, 184)
)
$g.DrawLines($pen, $points)
$g.DrawLine($pen, 98, 145, 158, 145)
$pngStream = New-Object System.IO.MemoryStream
$bmp.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
$png = $pngStream.ToArray()
$icoPath = Join-Path $assets "air-browser.ico"
$stream = [System.IO.File]::Create($icoPath)
$writer = New-Object System.IO.BinaryWriter($stream)
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]1)
$writer.Write([Byte]0)
$writer.Write([Byte]0)
$writer.Write([Byte]0)
$writer.Write([Byte]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]32)
$writer.Write([UInt32]$png.Length)
$writer.Write([UInt32]22)
$writer.Write($png)
$writer.Dispose()
$stream.Dispose()
$pngStream.Dispose()
$pen.Dispose()
$brush.Dispose()
$path.Dispose()
$g.Dispose()
$bmp.Dispose()
Write-Host "Generated valid AIR Browser icon: $icoPath"
