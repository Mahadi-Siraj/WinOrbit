# Verifies the island hides while a fullscreen app is in front and returns afterwards.
# Opens a borderless, fullscreen window (like a game or video) for ~4 seconds. Results go to -Out.
param([string]$Out = "$env:TEMP\winorbit-fullscreen-test.txt")
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System; using System.Runtime.InteropServices; using System.Text;
public static class FS {
  public delegate bool P(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(P p, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  public static bool IslandVisible() {
    bool v = false;
    EnumWindows((h, l) => { var sb = new StringBuilder(64); GetWindowText(h, sb, 64); if (sb.ToString() == "WinOrbit Island" && IsWindowVisible(h)) v = true; return true; }, IntPtr.Zero);
    return v;
  }
}
'@
$before = [FS]::IslandVisible()
$form = New-Object System.Windows.Forms.Form
$form.FormBorderStyle = 'None'; $form.WindowState = 'Maximized'; $form.TopMost = $true; $form.BackColor = 'Black'; $form.Text = 'Fullscreen test'; $form.ShowInTaskbar = $false
$script:during = $null
$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 4000
$timer.Add_Tick({ $timer.Stop(); $script:during = [FS]::IslandVisible(); $form.Close() })
$form.Add_Shown({ $form.Activate(); $timer.Start() })
[System.Windows.Forms.Application]::Run($form)
Start-Sleep -Seconds 4
$after = [FS]::IslandVisible()
$verdict = if ($before -and ($script:during -eq $false) -and $after) { "PASS" } else { "FAIL" }
"before=$before during=$($script:during) after=$after $verdict" | Set-Content $Out
