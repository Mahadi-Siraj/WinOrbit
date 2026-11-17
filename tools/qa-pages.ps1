# Visual QA: opens each Glasswave page in the running instance and captures the main window.
param([string]$Exe, [string]$OutDir, [string[]]$Pages = @("home","appearance","studio","wallpaper","island","widgets","rainmeter","tweaks","profiles","settings","about"))
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class QA {
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
}
'@
New-Item -ItemType Directory -Force $OutDir | Out-Null
foreach ($p in $Pages) {
  Start-Process $Exe -ArgumentList "--page", $p
  Start-Sleep -Milliseconds 1600
  $h = (Get-Process Glasswave | Select-Object -First 1).MainWindowHandle
  $r = New-Object QA+RECT
  [QA]::GetWindowRect($h, [ref]$r) | Out-Null
  $w = $r.R - $r.L; $hh = $r.B - $r.T
  if ($w -le 0) { Write-Output "no window for $p"; continue }
  $bmp = New-Object System.Drawing.Bitmap $w, $hh
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
  $bmp.Save((Join-Path $OutDir "$p.png"), [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  Write-Output "$p captured"
}
