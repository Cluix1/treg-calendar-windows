# Windows Architecture

## Native Stack

Use WinUI 3 and Windows App SDK. This keeps the app aligned with native Windows UI patterns and leaves room for platform-specific features such as:

- Toast notifications.
- System tray behavior.
- Startup tasks.
- Jump lists.
- Windows widgets later.

## Layers

```text
TregCalendar.Windows
  UI views and Windows-specific integrations

TregCalendar.Core
  event models
  mutation queue
  sync coordinator
  conflict resolution

TregCalendar.Data
  SQLite persistence
  local migrations

TregCalendar.Remote
  Supabase Auth
  Supabase REST or Edge Function sync client
```

## Web Parity

The Windows app should feel like the native version of the existing web calendar, not a separate product. Keep these pieces aligned unless there is a deliberate platform-specific reason to diverge:

- Account and data backend: same Supabase project, Auth users, Postgres event rows, and `sync-native-calendar` Edge Function.
- Sync contract: same event shape, mutation queue semantics, conflict response, and cursor behavior that macOS will reuse later.
- Visual direction: port the web app's default warm/off-white surfaces, muted taupe borders, and green accent into WinUI controls.

The current WinUI shell is intentionally compact: it has day/week/month range navigation, an agenda panel, quick local editing controls, and sync status. Future UI work should split this into dedicated view models and pages once the behavior settles.

## Offline Model

The app must never depend on a fresh network request to render the main calendar. It should:

1. Open from local SQLite.
2. Save edits to SQLite first.
3. Add each edit to the pending mutation queue.
4. Push pending mutations when online.
5. Pull remote updates after pending mutations are accepted.
6. Repeat automatically when connectivity changes.

The current Windows shell implements this foundation with startup sync, post-edit sync attempts, a five-minute retry timer, and a network-change retry hook. Failed sync attempts leave mutations in `pending_mutations`.

## Auth

Use Supabase Auth. Store tokens using Windows secure storage, not plain text files or localStorage equivalents.

The first version can use an embedded auth flow if it keeps session tokens in OS-protected storage. If the embedded auth flow becomes fragile, use the system browser with a custom redirect URI.

The sync service already depends on `IAccessTokenProvider`. The next auth implementation should satisfy that interface by reading the current Supabase access token from OS-protected storage.

The first Windows implementation uses:

- `SupabaseAuthClient` for Supabase Auth REST calls.
- `WindowsCredentialSessionStore` for Windows Password Vault token storage.
- `TREG_SUPABASE_PUBLISHABLE_KEY` for public Supabase client config.

Only the public anon/publishable key belongs in native client config. Service-role keys must remain server-side only.

## Security Rules

- Do not store Supabase service-role keys in the app.
- Do not trust client-supplied `owner_id`; the server must validate ownership.
- Every sync mutation must be authenticated.
- Each mutation must be idempotent using `client_mutation_id`.
