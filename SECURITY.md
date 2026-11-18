# Security policy

## Supported versions

Only the latest release of WinOrbit receives fixes. Please update before reporting.

| Version | Supported |
|---|---|
| Latest release | ✅ |
| Older releases | ❌ |

## Reporting a vulnerability

**Please do not open a public issue for security problems.**

Report it privately through GitHub instead:
[**Report a vulnerability**](https://github.com/Mahadi-Siraj/WinOrbit/security/advisories/new)
(Security tab → *Report a vulnerability*).

Useful details to include:

- the WinOrbit version and your Windows version/build,
- what an attacker could do and what they would need (local access, a crafted file, a network position…),
- steps to reproduce, or a proof of concept.

You can expect an acknowledgement within a week. Once a fix is released, the advisory is published and you are credited unless you prefer otherwise.

## Scope and design

WinOrbit is built to keep its attack surface small. Reports that break these promises are especially welcome:

- installs per user, with no admin rights and no services or drivers;
- no DLL injection into other processes and no patching of system files;
- no network connections by default — every optional online feature is opt-in and listed in the in-app request ledger ([privacy notes](docs/PRIVACY.md));
- no telemetry.

Out of scope: problems that need an attacker who already has admin rights or full control of the user's account, and SmartScreen warnings (releases are not code-signed, and the [release notes](.github/release-notes.md) say so).
