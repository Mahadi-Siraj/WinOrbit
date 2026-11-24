# Notes for AI coding agents

Read [docs/DEVELOPER-GUIDE.md](docs/DEVELOPER-GUIDE.md) first: it maps the code, explains how the Dynamic Island works, and gives step-by-step recipes for adding a live activity, an island tab or a setting. [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) explains why it is built this way.

## Commands

```powershell
Get-Process WinOrbit -ErrorAction SilentlyContinue | Stop-Process   # a running copy locks the exe
dotnet build WinOrbit.sln
dotnet test tests\WinOrbit.Tests                                    # must stay green
.\tools\build-release.ps1                                           # full release build into .\artifacts
```

WPF on .NET 10 (LTS), Windows only, no NuGet packages in the app (keep it that way). Target framework `net10.0-windows10.0.19041.0`, C# 14.

## Rules

* **Never** add DLL injection, hooks inside other processes, system-file patches, admin requirements or anything that weakens Windows security.
* **No network access by default.** Every request goes through the network gate in `Services/Network.cs`, behind its own off-by-default switch, and is listed in [docs/PRIVACY.md](docs/PRIVACY.md).
* Prefer Windows change notifications over polling. Stop any work nobody is looking at.
* P/Invoke goes in `Interop/Native.cs`.
* Put decision logic in small `internal static` functions and add unit tests for them (the test project sees internals).
* Settings: add properties with safe defaults to `Core/Models.cs`; old settings files must keep loading.
* Island changes must work on all four screen edges, every shape, and with Reduced Motion on.
* Update the user guide (and privacy audit, if relevant) with any user-visible change. Never call a placeholder finished.
* Builds are not code-signed; never say they are.

## Testing on a real machine

* User settings and data live in `%APPDATA%\WinOrbit\settings.json` and `data.json`. Back both up before a test that changes them and restore them afterwards.
* Do not toggle the user's real Wi-Fi, Bluetooth, airplane mode, dark mode or night light without asking. Use the island's `Demo()` previews (Settings › Dynamic Island › Preview) instead.
* `WinOrbit.exe --startup` starts it without opening the main window.

## Git and releases

* Never commit secrets, personal data, local settings files or the build output in `artifacts/`.
* Releasing = change `VERSION`, add a "New in x.y" section to `.github/release-notes.md`, push to `main`. GitHub Actions builds and publishes the release. Ask the owner before pushing or publishing.
