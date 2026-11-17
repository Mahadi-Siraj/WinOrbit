# Minimal input helper for automated UI testing: click, move, scroll, key.
param([string]$Action, [int]$X = 0, [int]$Y = 0, [int]$Delta = 0, [string]$Keys = "")
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class In {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, int d, UIntPtr e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint f, UIntPtr e);
}
'@
switch ($Action) {
  "click"  { [In]::SetCursorPos($X, $Y); Start-Sleep -Milliseconds 120; [In]::mouse_event(2,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 60; [In]::mouse_event(4,0,0,0,[UIntPtr]::Zero) }
  "rclick" { [In]::SetCursorPos($X, $Y); Start-Sleep -Milliseconds 120; [In]::mouse_event(8,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 60; [In]::mouse_event(16,0,0,0,[UIntPtr]::Zero) }
  "move"   { [In]::SetCursorPos($X, $Y) }
  "scroll" { [In]::SetCursorPos($X, $Y); Start-Sleep -Milliseconds 80; [In]::mouse_event(0x800,0,0,$Delta,[UIntPtr]::Zero) }
  "drag"   { $p = $Keys.Split(','); [In]::SetCursorPos($X, $Y); [In]::mouse_event(2,0,0,0,[UIntPtr]::Zero); for ($i=1; $i -le 20; $i++) { [In]::SetCursorPos($X + ([int]$p[0]-$X)*$i/20, $Y + ([int]$p[1]-$Y)*$i/20); Start-Sleep -Milliseconds 15 }; [In]::mouse_event(4,0,0,0,[UIntPtr]::Zero) }
  "keys"   { $vks = $Keys.Split('+') | ForEach-Object { [byte][Convert]::ToInt32($_, 16) }; foreach ($k in $vks) { [In]::keybd_event($k,0,0,[UIntPtr]::Zero) }; [Array]::Reverse($vks); foreach ($k in $vks) { [In]::keybd_event($k,0,2,[UIntPtr]::Zero) } }
}
