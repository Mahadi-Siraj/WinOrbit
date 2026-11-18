# Contributing to WinOrbit

Thanks for helping. Bug reports, ideas and pull requests are all welcome.

## Reporting bugs and ideas

- **Bugs:** [open a bug report](https://github.com/Mahadi-Siraj/WinOrbit/issues/new?template=bug_report.yml). Include your WinOrbit and Windows versions; a screenshot or the log from *About & Diagnostics → Open log folder* helps a lot.
- **Ideas:** [suggest a feature](https://github.com/Mahadi-Siraj/WinOrbit/issues/new?template=feature_request.yml), or start a thread in [Discussions](https://github.com/Mahadi-Siraj/WinOrbit/discussions).
- **Security problems:** please report privately — see [SECURITY.md](SECURITY.md).

## Building

You need Windows 10/11 and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
dotnet build WinOrbit.sln
dotnet test tests\WinOrbit.Tests
.\tools\build-release.ps1        # optional: installer + portable zip (needs Inno Setup 6)
```

[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) explains how the pieces fit together.

## Pull requests

1. Fork the repo and create a branch from `main`.
2. Keep the change focused — one fix or feature per pull request.
3. Add or update tests in `tests/WinOrbit.Tests` when you change behaviour.
4. Make sure `dotnet test` passes.
5. Describe what you changed and how you checked it; add a screenshot for anything visual.

## Ground rules

WinOrbit has a few promises every change has to keep:

- **Light:** no Electron or WebView, no polling while nothing is visible, and no new third-party packages without discussing it first.
- **Private:** no telemetry. Anything that goes online must be opt-in and show up in the request ledger.
- **Safe:** no admin rights, no DLL injection, no patching of system files, and no disabling of Windows security features.
- **Honest:** never present a placeholder or partial feature as finished. Mark it clearly in the UI and docs.

By contributing, you agree that your work is licensed under the [GPL-3.0](LICENSE), and that you follow the [Code of Conduct](CODE_OF_CONDUCT.md).
