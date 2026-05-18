# Windows Release Plan

## First Test Build

- Build an unsigned installer.
- Do not publish as production-ready.
- Expect Windows SmartScreen warnings until code signing is configured.

## Distribution

Use GitHub Releases on `Cluix1/treg-calendar-windows`.

Recommended artifact names:

```text
Treg-Calendar-Windows-x64.exe
Treg-Calendar-Windows-arm64.exe
```

The Treg hub should link directly to the latest release assets once builds exist.

## Signing Later

Before a public launch, add:

- Windows code signing certificate.
- Release workflow that signs installers.
- SHA256 checksums attached to releases.
