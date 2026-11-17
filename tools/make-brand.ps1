# Renders the WinOrbit icon (multi-size .ico + PNGs) and the README banner with WPF vector drawing.
# Run: pwsh -STA -File tools/make-brand.ps1 -IcoOut src/WinOrbit/Assets/winorbit.ico -AssetsDir docs/assets
param([string]$IcoOut, [string]$AssetsDir)
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$ErrorActionPreference = 'Stop'
function C([string]$hex) { [System.Windows.Media.ColorConverter]::ConvertFromString($hex) }
function Brush([string]$hex) { $b = New-Object System.Windows.Media.SolidColorBrush (C $hex); $b.Freeze(); $b }
function Lin($stops, [double]$x1 = 0, [double]$y1 = 0, [double]$x2 = 1, [double]$y2 = 1) {
  $g = New-Object System.Windows.Media.LinearGradientBrush; $g.StartPoint = [System.Windows.Point]::new($x1, $y1); $g.EndPoint = [System.Windows.Point]::new($x2, $y2)
  foreach ($s in $stops) { $g.GradientStops.Add((New-Object System.Windows.Media.GradientStop (C $s[0]), $s[1])) }; $g.Freeze(); $g }
function Rad($stops) {
  $g = New-Object System.Windows.Media.RadialGradientBrush
  foreach ($s in $stops) { $g.GradientStops.Add((New-Object System.Windows.Media.GradientStop (C $s[0]), $s[1])) }; $g.Freeze(); $g }
function Pen($brush, [double]$w) { $p = New-Object System.Windows.Media.Pen $brush, $w; $p.StartLineCap = 'Round'; $p.EndLineCap = 'Round'; $p.Freeze(); $p }
function RR([double]$x, [double]$y, [double]$w, [double]$h, [double]$r) { New-Object System.Windows.Media.RectangleGeometry ([System.Windows.Rect]::new($x, $y, $w, $h)), $r, $r }
function P([double]$x, [double]$y) { [System.Windows.Point]::new($x, $y) }

# ---- the mark: a glass island on a black tile, circled by a thin orbit with one satellite (256×256 space) ----
function Draw-Icon($dc, [bool]$tile = $true, [double]$bold = 1) {
  $box = RR 12 12 232 232 58
  if ($tile) {
    $dc.DrawGeometry((Lin @(@('#FF15161B', 0), @('#FF050506', 1)) 0 0 0 1), $null, $box)
    $dc.PushClip($box)
  }
  # a faint colour bloom behind the island — the only colour besides the orbit
  $dc.DrawEllipse((Rad @(@('#3346E3FF', 0), @('#147B5CFF', 0.55), @('#00000000', 1))), $null, (P 128 136), 120, 70)
  $tilt = New-Object System.Windows.Media.RotateTransform -17, 128, 128
  $rx = 104; $ry = 30
  # back half of the orbit (dim), passes behind the island
  $dc.PushTransform($tilt)
  $dc.PushClip((New-Object System.Windows.Media.RectangleGeometry ([System.Windows.Rect]::new(0, 0, 256, 128))))
  $dc.DrawEllipse($null, (Pen (Brush '#40FFFFFF') (2.2 * $bold)), (P 128 128), $rx, $ry)
  $dc.Pop(); $dc.Pop()
  # the island: glossy black glass capsule with a lit rim and a camera dot
  $pill = RR 62 104 132 48 24
  $dc.DrawEllipse((Rad @(@('#66000000', 0), @('#00000000', 1))), $null, (P 128 160), 84, 14)
  $dc.DrawGeometry((Lin @(@('#FF2E3039', 0), @('#FF0B0C10', 0.55), @('#FF050608', 1)) 0 0 0 1), (Pen (Lin @(@('#D9FFFFFF', 0), @('#26FFFFFF', 0.5), @('#4DFFFFFF', 1)) 0 0 0 1) (2 * $bold)), $pill)
  $dc.PushClip($pill)
  $dc.DrawGeometry((Lin @(@('#2EFFFFFF', 0), @('#00FFFFFF', 1)) 0 0 0 1), $null, (RR 62 104 132 22 11))
  $dc.Pop()
  $dc.DrawEllipse((Brush '#FF161923'), (Pen (Brush '#33FFFFFF') 1), (P 172 128), 6.5, 6.5)
  $dc.DrawEllipse((Brush '#AA6FA8FF'), $null, (P 170.5 126.5), 2, 2)
  # front half of the orbit (bright, cyan → violet), crosses in front of the island
  $dc.PushTransform($tilt)
  $dc.PushClip((New-Object System.Windows.Media.RectangleGeometry ([System.Windows.Rect]::new(0, 128, 256, 128))))
  $dc.DrawEllipse($null, (Pen (Lin @(@('#33FFFFFF', 0), @('#FF8FF6FF', 0.45), @('#FFFFFFFF', 0.62), @('#FFA98BFF', 1)) 0 0 1 0) (2.6 * $bold)), (P 128 128), $rx, $ry)
  $dc.Pop()
  # the satellite riding the orbit
  $a = 32 * [Math]::PI / 180
  $sx = 128 + $rx * [Math]::Cos($a); $sy = 128 + $ry * [Math]::Sin($a)
  $dc.DrawEllipse((Rad @(@('#CC8FF6FF', 0), @('#008FF6FF', 1))), $null, (P $sx $sy), 16, 16)
  $dc.DrawEllipse((Brush '#FFFFFFFF'), $null, (P $sx $sy), (5.5 * [Math]::Sqrt($bold)), (5.5 * [Math]::Sqrt($bold)))
  $dc.Pop()
  if ($tile) {
    $dc.Pop()
    # thin light rim around the tile, brightest at the top
    $dc.DrawGeometry($null, (Pen (Lin @(@('#73FFFFFF', 0), @('#14FFFFFF', 0.45), @('#26FFFFFF', 1)) 0 0 0 1) (2 * $bold)), $box)
  }
}

# Tray / title-bar sizes: no tile — a bright island and orbit on transparent, readable on dark and light taskbars.
function Draw-Tiny($dc) {
  $tilt = New-Object System.Windows.Media.RotateTransform -17, 128, 128
  $dc.PushTransform($tilt)
  $dc.PushClip((New-Object System.Windows.Media.RectangleGeometry ([System.Windows.Rect]::new(0, 0, 256, 128))))
  $dc.DrawEllipse($null, (Pen (Brush '#B38FF6FF') 16), (P 128 128), 112, 40)
  $dc.Pop(); $dc.Pop()
  $dc.DrawGeometry((Brush '#FFF4F6FA'), (Pen (Brush '#FF1A1C24') 6), (RR 52 92 152 72 36))
  $dc.PushTransform($tilt)
  $dc.PushClip((New-Object System.Windows.Media.RectangleGeometry ([System.Windows.Rect]::new(0, 128, 256, 128))))
  $dc.DrawEllipse($null, (Pen (Lin @(@('#FF5FEFFF', 0), @('#FFA98BFF', 1)) 0 0 1 0) 18), (P 128 128), 112, 40)
  $dc.Pop(); $dc.Pop()
}

function Render-Icon([int]$size) {
  if ($size -le 20) {
    $tv = New-Object System.Windows.Media.DrawingVisual
    $tc = $tv.RenderOpen()
    $tc.PushTransform((New-Object System.Windows.Media.ScaleTransform ($size / 256.0), ($size / 256.0)))
    Draw-Tiny $tc
    $tc.Pop(); $tc.Close()
    $tb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $tb.Render($tv); return $tb
  }
  $dv = New-Object System.Windows.Media.DrawingVisual
  $dc = $dv.RenderOpen()
  $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform ($size / 256.0), ($size / 256.0)))
  # tiny sizes (tray, title bar) get bolder lines so the orbit stays visible
  $bold = if ($size -le 16) { 3.4 } elseif ($size -le 24) { 2.6 } elseif ($size -le 32) { 2.0 } elseif ($size -le 48) { 1.5 } else { 1 }
  Draw-Icon $dc $true $bold
  $dc.Pop(); $dc.Close()
  $bmp = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
  $bmp.Render($dv); $bmp
}

function Png-Bytes($bitmap) {
  $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
  $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
  $ms = New-Object System.IO.MemoryStream; $enc.Save($ms); $ms.ToArray()
}

New-Item -ItemType Directory -Force $AssetsDir | Out-Null
$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256

# Classic DIB icon entry (BITMAPINFOHEADER + bottom-up BGRA + empty AND mask): readable by every part of Windows.
function Dib-Bytes($bitmap) {
  $conv = New-Object System.Windows.Media.Imaging.FormatConvertedBitmap $bitmap, ([System.Windows.Media.PixelFormats]::Bgra32), $null, 0
  $s = $conv.PixelWidth; $stride = $s * 4
  $px = New-Object byte[] ($stride * $s); $conv.CopyPixels($px, $stride, 0)
  $ms = New-Object System.IO.MemoryStream; $bw = New-Object System.IO.BinaryWriter $ms
  $bw.Write([int]40); $bw.Write([int]$s); $bw.Write([int]($s * 2)); $bw.Write([int16]1); $bw.Write([int16]32)
  $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
  for ($y = $s - 1; $y -ge 0; $y--) { $bw.Write($px, $y * $stride, $stride) }
  $maskRow = [int]([Math]::Ceiling($s / 32.0) * 4)
  $bw.Write((New-Object byte[] ($maskRow * $s)))
  $ms.ToArray()
}
$images = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($s in $sizes) {
  [byte[]]$bytes = if ($s -ge 256) { Png-Bytes (Render-Icon $s) } else { Dib-Bytes (Render-Icon $s) }
  $images.Add($bytes)
}
$out = New-Object System.IO.MemoryStream; $w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $s = $sizes[$i]; $len = $images[$i].Length
  $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256)); $w.Write([byte]0); $w.Write([byte]0)
  $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$len); $w.Write([uint32]$offset); $offset += $len
}
foreach ($img in $images) { $w.Write($img) }
[System.IO.File]::WriteAllBytes($IcoOut, $out.ToArray())
[System.IO.File]::WriteAllBytes((Join-Path $AssetsDir 'winorbit-512.png'), (Png-Bytes (Render-Icon 512)))
"icon: $IcoOut"

# ---- README banner 1280×640: black, with the orbit sweeping across ----
$dv = New-Object System.Windows.Media.DrawingVisual
$dc = $dv.RenderOpen()
$dc.DrawRectangle((Brush '#FF040405'), $null, [System.Windows.Rect]::new(0, 0, 1280, 640))
$dc.DrawEllipse((Rad @(@('#2246E3FF', 0), @('#00000000', 1))), $null, (P 300 330), 420, 300)
$dc.DrawEllipse((Rad @(@('#1C7B5CFF', 0), @('#00000000', 1))), $null, (P 1000 300), 460, 320)
$big = New-Object System.Windows.Media.RotateTransform -8, 640, 330
$dc.PushTransform($big)
$dc.DrawEllipse($null, (Pen (Lin @(@('#00FFFFFF', 0), @('#2EFFFFFF', 0.3), @('#14FFFFFF', 0.7), @('#00FFFFFF', 1)) 0 0 1 0) 1.4), (P 640 330), 620, 150)
$dc.Pop()
$dc.PushTransform((New-Object System.Windows.Media.TranslateTransform 120, 202))
Draw-Icon $dc
$dc.Pop()
$face = New-Object System.Windows.Media.Typeface ((New-Object System.Windows.Media.FontFamily 'Segoe UI Variable Display, Segoe UI'), 'Normal', 'SemiBold', 'Normal')
$faceReg = New-Object System.Windows.Media.Typeface ((New-Object System.Windows.Media.FontFamily 'Segoe UI Variable Text, Segoe UI'), 'Normal', 'Normal', 'Normal')
function Text($s, $tf, [double]$size, $brush) { New-Object System.Windows.Media.FormattedText $s, ([System.Globalization.CultureInfo]::InvariantCulture), 'LeftToRight', $tf, $size, $brush, 1.0 }
$win = Text 'Win' $face 118 (Brush '#FFFFFFFF')
$dc.DrawText($win, (P 432 178))
$orbit = Text 'Orbit' $face 118 (Lin @(@('#FF8FF6FF', 0), @('#FFFFFFFF', 0.5), @('#FFA98BFF', 1)) 0 0 1 0)
$dc.DrawText($orbit, (P (432 + $win.WidthIncludingTrailingWhitespace - 2) 178))
$tag = Text 'The Dynamic Island and desktop suite Windows deserves.' $faceReg 29 (Brush '#CCFFFFFF')
$dc.DrawText($tag, (P 438 340))
$x = 438
foreach ($c in 'Free & open source', '~60 MB RAM', '~0.1% CPU', 'Zero telemetry') {
  $t = Text $c $faceReg 21 (Brush '#E6FFFFFF')
  $wdt = $t.Width + 34
  $dc.DrawGeometry((Brush '#14FFFFFF'), (Pen (Brush '#33FFFFFF') 1.2), (RR $x 408 $wdt 42 21))
  $dc.DrawText($t, (P ($x + 17) 416))
  $x += $wdt + 12
}
$dc.Close()
$banner = New-Object System.Windows.Media.Imaging.RenderTargetBitmap 1280, 640, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
$banner.Render($dv)
[System.IO.File]::WriteAllBytes((Join-Path $AssetsDir 'banner.png'), (Png-Bytes $banner))
"banner: $(Join-Path $AssetsDir 'banner.png')"
