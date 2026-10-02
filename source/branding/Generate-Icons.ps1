# Rebuild original SpaceTranslate artwork without external packages.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetDir = Join-Path $PSScriptRoot '../src/Assets'
New-Item -ItemType Directory -Path $assetDir -Force | Out-Null
function RoundRect($x, $y, $w, $h, $r) {
    $p = [Drawing.Drawing2D.GraphicsPath]::new()
    $d = 2 * $r
    $p.AddArc($x,$y,$d,$d,180,90); $p.AddArc(($x+$w-$d),$y,$d,$d,270,90)
    $p.AddArc(($x+$w-$d),($y+$h-$d),$d,$d,0,90); $p.AddArc($x,($y+$h-$d),$d,$d,90,90)
    $p.CloseFigure()
    return $p
}
function RenderIcon([int]$size, [bool]$paused) {
    $large = [Drawing.Bitmap]::new(($size*4),($size*4))
    $g = [Drawing.Graphics]::FromImage($large)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.ScaleTransform(($size*4/256.0),($size*4/256.0))
    $base = if ($paused) { '#677585' } else { '#087FB8' }
    $dark = if ($paused) { '#485360' } else { '#075780' }
    $face = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($base))
    $depth = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($dark))
    $bottom = RoundRect 16 48 224 176 40
    $top = RoundRect 16 32 224 176 40
    $g.FillPath($depth,$bottom); $g.FillPath($face,$top)
    # One space character, drawn geometrically so installed fonts do not matter.
    $pen = [Drawing.Pen]::new([Drawing.Color]::White,16)
    $pen.StartCap = $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    $points = [Drawing.PointF[]]@([Drawing.PointF]::new(72,108),[Drawing.PointF]::new(72,148),[Drawing.PointF]::new(184,148),[Drawing.PointF]::new(184,108))
    $g.DrawLines($pen,$points)
    if ($paused) {
        $g.FillEllipse($depth,166,154,84,84)
        $g.FillRectangle([Drawing.Brushes]::White,190,176,10,38)
        $g.FillRectangle([Drawing.Brushes]::White,212,176,10,38)
    }
    $small = [Drawing.Bitmap]::new($size,$size)
    $sg = [Drawing.Graphics]::FromImage($small)
    $sg.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $sg.DrawImage($large,0,0,$size,$size)
    $sg.Dispose(); $g.Dispose(); $large.Dispose(); $pen.Dispose(); $face.Dispose(); $depth.Dispose(); $bottom.Dispose(); $top.Dispose()
    return $small
}
foreach ($variant in @('app','paused')) {
    $sizes = @(16,20,24,32,40,48,64,128,256)
    $frames = @()
    foreach ($size in $sizes) {
        $bitmap = RenderIcon $size ($variant -eq 'paused')
        $memory = [IO.MemoryStream]::new()
        $bitmap.Save($memory,[Drawing.Imaging.ImageFormat]::Png)
        $frames += ,$memory.ToArray()
        $memory.Dispose(); $bitmap.Dispose()
    }
    $stream = [IO.File]::Create((Join-Path $assetDir "$variant.ico"))
    $writer = [IO.BinaryWriter]::new($stream)
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i=0; $i -lt $sizes.Count; $i++) {
        $dim = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dim); $writer.Write([byte]$dim); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
    $writer.Dispose(); $stream.Dispose()
}
$preview = RenderIcon 512 $false
$preview.Save((Join-Path $assetDir 'logo.png'),[Drawing.Imaging.ImageFormat]::Png)
$preview.Dispose()
