# Idle benchmark: samples WinOrbit's CPU time and memory over a period and reports averages.
param([int]$Seconds = 300, [int]$Interval = 10, [string]$Out = "bench.json")
$p = Get-Process WinOrbit -ErrorAction Stop | Select-Object -First 1
$cores = [Environment]::ProcessorCount
$startCpu = $p.TotalProcessorTime; $startAt = Get-Date
$samples = @()
for ($t = 0; $t -lt $Seconds; $t += $Interval) {
  Start-Sleep -Seconds $Interval
  $p.Refresh()
  $samples += [pscustomobject]@{ t = $t + $Interval; privateMB = [math]::Round($p.PrivateMemorySize64 / 1MB, 1); workingSetMB = [math]::Round($p.WorkingSet64 / 1MB, 1); threads = $p.Threads.Count; handles = $p.HandleCount }
}
$p.Refresh()
$elapsed = ((Get-Date) - $startAt).TotalSeconds
$cpuSec = ($p.TotalProcessorTime - $startCpu).TotalSeconds
$result = [pscustomobject]@{
  seconds = [math]::Round($elapsed); cores = $cores
  cpuSeconds = [math]::Round($cpuSec, 3)
  avgCpuPercentOfMachine = [math]::Round(100 * $cpuSec / $elapsed / $cores, 3)
  avgCpuPercentOfOneCore = [math]::Round(100 * $cpuSec / $elapsed, 3)
  privateMB_min = ($samples | Measure-Object privateMB -Minimum).Minimum; privateMB_max = ($samples | Measure-Object privateMB -Maximum).Maximum
  workingSetMB_avg = [math]::Round(($samples | Measure-Object workingSetMB -Average).Average, 1); workingSetMB_max = ($samples | Measure-Object workingSetMB -Maximum).Maximum
  handles_first = $samples[0].handles; handles_last = $samples[-1].handles
  samples = $samples
}
$result | ConvertTo-Json -Depth 4 | Set-Content $Out
$result | Select-Object seconds, cpuSeconds, avgCpuPercentOfMachine, avgCpuPercentOfOneCore, privateMB_min, privateMB_max, workingSetMB_avg, workingSetMB_max, handles_first, handles_last | Format-List
