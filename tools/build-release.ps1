# Builds everything a release needs:
#   artifacts\installer\WinOrbit-Setup-<ver>.exe        per-user installer (needs .NET 8 Desktop Runtime)
#   artifacts\WinOrbit-Portable-<ver>-win-x64.zip        self-contained, no runtime needed, runs from any folder
param([string]$Version = "", [switch]$SkipTests)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
if (-not $Version) { $Version = (Get-Content (Join-Path $root "VERSION") -Raw).Trim() }   # single source of truth
$env:DOTNET_NOLOGO = "1"; $env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"   # keep the .NET CLI itself from sending telemetry

if (-not $SkipTests) {
  dotnet test tests\WinOrbit.Tests\WinOrbit.Tests.csproj -c Release --nologo -v q
  if ($LASTEXITCODE -ne 0) { throw "Tests failed" }
}

Remove-Item artifacts -Recurse -Force -ErrorAction SilentlyContinue
$common = @("src\WinOrbit\WinOrbit.csproj", "-c", "Release", "-r", "win-x64", "--nologo", "-v", "q", "-p:Version=$Version", "-p:DebugType=none")

# 1) Framework-dependent, ReadyToRun (fast start, ~small) for the installer
dotnet publish @common --self-contained false -p:PublishReadyToRun=true -o artifacts\publish\fdd
if ($LASTEXITCODE -ne 0) { throw "Publish (installer) failed" }

# 2) Self-contained single file for the portable package
dotnet publish @common --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishReadyToRun=true -o artifacts\publish\portable
if ($LASTEXITCODE -ne 0) { throw "Publish (portable) failed" }
Set-Content artifacts\publish\portable\portable.flag "WinOrbit keeps its settings in the Data folder next to the exe while this file exists."
Copy-Item README.md, docs\PRIVACY.md, docs\USER-GUIDE.md artifacts\publish\portable\ -ErrorAction SilentlyContinue
Compress-Archive -Path artifacts\publish\portable\* -DestinationPath "artifacts\WinOrbit-Portable-$Version-win-x64.zip" -Force

# 3) Installer
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($iscc) {
  & $iscc /Q "/DAppVersion=$Version" "/DPublishDir=$root\artifacts\publish\fdd" installer\WinOrbit.iss
  if ($LASTEXITCODE -ne 0) { throw "Installer build failed" }
} else { Write-Warning "Inno Setup not found; skipped the installer. The portable zip was still built." }

Get-ChildItem artifacts -File -Recurse | Where-Object { $_.Extension -in ".exe", ".zip" -and $_.DirectoryName -notmatch "publish" } |
  ForEach-Object { "{0,-50} {1,8:N1} MB  SHA256 {2}" -f $_.Name, ($_.Length / 1MB), (Get-FileHash $_.FullName).Hash }
