$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$root=Split-Path $PSScriptRoot -Parent
New-Item -ItemType Directory -Force (Join-Path $root 'assets') | Out-Null
$bitmap=[System.Drawing.Bitmap]::new(64,64)
$graphics=[System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode=[System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::FromArgb(20,27,32))
$pen=[System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(100,227,183),4)
$points=[System.Drawing.PointF[]]@([System.Drawing.PointF]::new(32,9),[System.Drawing.PointF]::new(55,32),[System.Drawing.PointF]::new(32,55),[System.Drawing.PointF]::new(9,32))
$graphics.DrawPolygon($pen,$points)
$points=[System.Drawing.PointF[]]@([System.Drawing.PointF]::new(32,22),[System.Drawing.PointF]::new(42,32),[System.Drawing.PointF]::new(32,42),[System.Drawing.PointF]::new(22,32))
$brush=[System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(100,227,183))
$graphics.FillPolygon($brush,$points)
$icon=[System.Drawing.Icon]::FromHandle($bitmap.GetHicon())
$stream=[IO.File]::Create((Join-Path $root 'assets\Thermal.ico'))
$icon.Save($stream)
$stream.Dispose(); $icon.Dispose(); $graphics.Dispose(); $bitmap.Dispose(); $pen.Dispose(); $brush.Dispose()
