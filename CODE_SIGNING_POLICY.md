# Code signing policy

**Status: not signed yet.** WinOrbit is applying for free code signing for open-source projects from SignPath Foundation. Until that is approved, releases (1.3.1 and earlier) are **not code-signed**, and Windows SmartScreen may warn on first install. This page will say when signing starts and from which version.

Once approved:

> Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org)

## What gets signed

Only files built from this repository's own source code, by GitHub Actions, from a commit on `main`:

* `WinOrbit.exe` (inside the installer and the portable zip)
* the installer, `WinOrbit-Setup-<version>.exe`

Third-party files are never signed with this certificate. The .NET runtime files in the portable zip keep Microsoft's own signatures.

Every build comes from the release workflow ([`.github/workflows/release.yml`](.github/workflows/release.yml)), which runs the tests, builds the installer and portable zip, and publishes them with SHA-256 checksums. Nothing is built or signed on a personal computer.

## Team and roles

WinOrbit is maintained by one person.

| Role | Who |
|---|---|
| Author (commits to the repository) | [Mahadi-Siraj](https://github.com/Mahadi-Siraj) |
| Reviewer (reviews every change from anyone else before it is merged) | [Mahadi-Siraj](https://github.com/Mahadi-Siraj) |
| Approver (approves each signing request, one per release) | [Mahadi-Siraj](https://github.com/Mahadi-Siraj) |

Contributions from others arrive as pull requests and are reviewed before merging. Two-factor authentication is required on GitHub and SignPath for every account with access.

## Privacy

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

The optional online features (weather, synced lyrics, rain alerts, the "Check now" update check) are off by default, each has its own switch, and every request is listed in the [privacy audit](docs/PRIVACY.md).

## Uninstalling

The installer adds WinOrbit to **Settings › Apps › Installed apps**, where it can be uninstalled like any other app. The portable version is removed by deleting its folder.
