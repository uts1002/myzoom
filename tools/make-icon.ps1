# Generates Assets\myzoom.ico (PNG-compressed ICO, 16-256px).
# Usage: powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
Add-Type -AssemblyName System.Drawing

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    # rounded square background
    $r = [single]($s * 0.22)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $w = [single]($s - 1)
    $path.AddArc(0, 0, $r * 2, $r * 2, 180, 90)
    $path.AddArc($w - $r * 2, 0, $r * 2, $r * 2, 270, 90)
    $path.AddArc($w - $r * 2, $w - $r * 2, $r * 2, $r * 2, 0, 90)
    $path.AddArc(0, $w - $r * 2, $r * 2, $r * 2, 90, 90)
    $path.CloseFigure()
    $bg = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 30, 41, 59))
    $g.FillPath($bg, $path)

    # magnifier lens
    $lw = [single]([Math]::Max(1.5, $s * 0.09))
    $pen = New-Object System.Drawing.Pen -ArgumentList @([System.Drawing.Color]::White, $lw)
    $cx = $s * 0.43; $cy = $s * 0.43; $rad = $s * 0.24
    $g.DrawEllipse($pen, [single]($cx - $rad), [single]($cy - $rad), [single]($rad * 2), [single]($rad * 2))

    # handle
    $pen2 = New-Object System.Drawing.Pen -ArgumentList @([System.Drawing.Color]::White, [single]($lw * 1.5))
    $pen2.StartCap = 'Round'; $pen2.EndCap = 'Round'
    $g.DrawLine($pen2, [single]($cx + $rad * 0.72), [single]($cy + $rad * 0.72), [single]($s * 0.80), [single]($s * 0.80))

    # red pen stroke inside lens
    $pen3 = New-Object System.Drawing.Pen -ArgumentList @([System.Drawing.Color]::FromArgb(255, 239, 68, 68), [single]([Math]::Max(1.2, $s * 0.075)))
    $pen3.StartCap = 'Round'; $pen3.EndCap = 'Round'
    $g.DrawBezier($pen3, [single]($cx - $rad * 0.55), [single]($cy + $rad * 0.15), [single]($cx - $rad * 0.2), [single]($cy - $rad * 0.6), [single]($cx + $rad * 0.15), [single]($cy + $rad * 0.6), [single]($cx + $rad * 0.55), [single]($cy - $rad * 0.15))

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $pngs += , $ms.ToArray()
}

$out = Join-Path $PSScriptRoot '..\Assets\myzoom.ico'
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$pngs[$i].Length); $bw.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()
Write-Host "icon written: $out"
