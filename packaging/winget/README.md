# winget manifest (prepared, not submitted)

These files describe the published **v1.3.1** release for the Windows Package Manager, so people could install WinOrbit with:

```powershell
winget install Mahadi-Siraj.WinOrbit
```

**Status: not submitted.** Nothing here has been sent to the winget community repository. Submitting is the owner's decision.

## Check them locally

```powershell
winget validate --manifest packaging\winget\1.3.1
winget install --manifest packaging\winget\1.3.1     # needs: winget settings --enable LocalManifestFiles (admin, once)
```

## Submit (when the owner decides to)

1. Fork `microsoft/winget-pkgs`.
2. Copy the `1.3.1` folder to `manifests/m/Mahadi-Siraj/WinOrbit/1.3.1/`.
3. Open a pull request. Microsoft's bots download the installer and check the SHA-256 against `InstallerSha256`.

## For each new release

* Copy the folder to the new version number and change `PackageVersion`, `InstallerUrl`, `ReleaseDate`, `ReleaseNotesUrl`.
* `InstallerSha256` must be the hash of the installer **GitHub Actions published** (from that release's `SHA256SUMS.txt`), not of a local build — local builds differ byte for byte.
* `ProductCode` is Inno Setup's `{AppId}_is1` and never changes.

Notes: the installer is per-user (`Scope: user`, no admin) and needs the .NET Desktop Runtime, declared as a dependency so winget installs it first. 1.3.x needs .NET 8 (`Microsoft.DotNet.DesktopRuntime.8`); from 1.4 it is .NET 10, so a 1.4 manifest must declare `Microsoft.DotNet.DesktopRuntime.10` instead. Builds are not code-signed.
