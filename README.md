# Treg Calendar for Windows

Native Windows app for Treg Calendar.

## Direction

- UI: WinUI 3 / Windows App SDK.
- Language: C#.
- Local storage: SQLite.
- Remote backend: Supabase Auth, Postgres, and Edge Functions.
- Installer target: unsigned `.exe` installer first, signed installer later.

The first engineering milestone is not UI. It is the offline sync foundation:

```text
edit locally -> save to SQLite -> enqueue mutation -> sync in background -> merge remote changes
```

## Setup

This repo needs Visual Studio with the Windows App SDK workload/templates. The current local machine has .NET runtimes installed but no .NET SDK, so project generation/building should be completed after installing the native Windows toolchain.

Recommended tooling:

- Visual Studio 2022 or newer.
- .NET SDK.
- Windows App SDK / WinUI 3 templates.
- SQLite tooling.

## Docs

- [Architecture](docs/architecture.md)
- [Sync Contract](docs/sync-contract.md)
- [Release Plan](docs/release-plan.md)

## Related Repos

- Hub: `Cluix1/treg-hub`
- Web calendar: `Cluix1/treg-calendar`
- macOS app: `Cluix1/treg-calendar-macos`
