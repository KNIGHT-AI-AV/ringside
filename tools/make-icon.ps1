# Draws assets/ringside.ico (16-256 px, PNG entries) and assets/ringside.png (512 px):
# a Claude-coloured arc outside, a Codex-coloured arc inside, on a dark tile.
param(
    [string]$Out = (Join-Path (Split-Path $PSScriptRoot) 'assets/ringside.ico'),
    [string]$Png = (Join-Path (Split-Path $PSScriptRoot) 'assets/ringside.png'))
Add-Type -AssemblyName System.Drawing

function Draw([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $k = $s / 256.0
    $tile = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r = 60 * $k; $w = $s - 1
    $tile.AddArc(0, 0, 2*$r, 2*$r, 180, 90); $tile.AddArc($w-2*$r, 0, 2*$r, 2*$r, 270, 90)
    $tile.AddArc($w-2*$r, $w-2*$r, 2*$r, 2*$r, 0, 90); $tile.AddArc(0, $w-2*$r, 2*$r, 2*$r, 90, 90); $tile.CloseFigure()
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Point 0,0), (New-Object System.Drawing.Point 0,$s), ([System.Drawing.Color]::FromArgb(255,36,36,42)), ([System.Drawing.Color]::FromArgb(255,20,20,24))
    $g.FillPath($bg, $tile)
    foreach ($ring in @(@{d=164; w=26; p=0.72; c=[System.Drawing.Color]::FromArgb(255,0xD9,0x77,0x57)},
                       @{d=84;  w=26; p=0.46; c=[System.Drawing.Color]::FromArgb(255,0x19,0xC3,0x9C)})) {
        $d = $ring.d * $k; $pw = [Math]::Max(1.6, $ring.w * $k); $o = ($s - $d) / 2
        $rect = New-Object System.Drawing.RectangleF $o, $o, $d, $d
        $track = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(46,255,255,255)), $pw
        $g.DrawEllipse($track, $rect)
        $pen = New-Object System.Drawing.Pen $ring.c, $pw
        $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
        $g.DrawArc($pen, $rect, -90, [single](360 * $ring.p))
    }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    , $ms.ToArray()
}

$sizes = 256, 64, 48, 40, 32, 24, 20, 16
$images = foreach ($s in $sizes) { , (Draw $s) }
$fs = [System.IO.File]::Create($Out); $bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $images[$i].Length
    $bw.Write([byte]($s % 256)); $bw.Write([byte]($s % 256)); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
    $offset += $len
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Close()
[System.IO.File]::WriteAllBytes($Png, (Draw 512))
"wrote $Out and $Png"
