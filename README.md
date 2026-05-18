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

This repo needs Visual Studio with the Windows App SDK workload/templates.

Recommended tooling:

- Visual Studio 2022 or newer.
- .NET SDK.
- Windows App SDK / WinUI 3 templates.
- SQLite tooling.

## Current Status

The initial WinUI 3 packaged app has been scaffolded and runs as a blank native window. It targets:

- Target framework: `net8.0-windows10.0.19041.0`
- Minimum Windows version: `10.0.17763.0` (Windows 10 version 1809)

The first local sync foundation is in place:

- `local_events`, `pending_mutations`, and `sync_state` SQLite tables are created on app launch.
- Local event writes can be saved with a pending mutation in the same SQLite transaction.
- Pending mutations can be read in sync-sized batches, marked accepted, or marked failed for retry visibility.

The first sync service slice is also in place:

- `NativeSyncClient` posts queued mutations to the deployed Supabase Edge Function.
- `CalendarSyncService` tracks a stable native client ID, sends pending mutations, applies accepted mutations, records conflicts/errors, stores `last_sync_cursor`, and merges returned remote events into SQLite.

The first native auth slice is in place:

- The scaffold window supports email/password login, logout, and manual sync.
- Supabase sessions are stored with Windows Password Vault.
- The sync service reads access tokens through `IAccessTokenProvider`.

The first local editing slice is in place:

- Quick-add creates an event in SQLite and queues a `create` mutation.
- Selecting a synced event lets you save a title change or queue a delete.
- New local events must sync once before edit/delete mutations are allowed, because the server needs a remote event ID for those operations.

The first automatic sync behavior is in place:

- Local edits try to sync immediately after being queued.
- The app syncs on startup when a saved session exists.
- The app retries sync every five minutes while open.
- The app retries when Windows reports network connectivity has changed.
- The UI shows whether local changes are waiting to sync.

Before using login locally, set the public Supabase publishable key:

```powershell
[Environment]::SetEnvironmentVariable("TREG_SUPABASE_PUBLISHABLE_KEY", "<public anon or publishable key>", "User")
```

Then restart Visual Studio so the running app inherits the new environment variable.

## Docs

- [Architecture](docs/architecture.md)
- [Sync Contract](docs/sync-contract.md)
- [Release Plan](docs/release-plan.md)

## Related Repos

- Hub: `Cluix1/treg-hub`
- Web calendar: `Cluix1/treg-calendar`
- macOS app: `Cluix1/treg-calendar-macos`
