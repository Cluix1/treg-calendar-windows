# Native Sync Contract

This is the shared protocol for native Windows and macOS clients.

## Goals

- Offline app launch.
- Offline create/edit/delete.
- Automatic sync when the network returns.
- No app restart required after reconnecting.
- Same account and data model as the web app.

## Local Tables

### `local_events`

Stores the last known event state.

Required fields:

- `id`: remote UUID when known.
- `local_id`: local UUID generated before remote insert.
- `calendar_id`
- `owner_id`
- `title`
- `description_html`
- `location`
- `starts_at`
- `ends_at`
- `due_at`
- `all_day`
- `all_day_date`
- `rrule`
- `course_name`
- `course_color`
- `status`
- `deleted_at`
- `remote_updated_at`
- `local_updated_at`
- `sync_state`: `synced`, `pending`, `conflict`, or `deleted`

### `pending_mutations`

Stores local edits waiting to sync.

Required fields:

- `client_mutation_id`: UUID, stable across retries.
- `entity_type`: currently `event`.
- `entity_local_id`
- `entity_remote_id`
- `operation`: `create`, `update`, or `delete`.
- `base_remote_updated_at`: remote timestamp seen before the edit.
- `payload_json`
- `created_at`
- `attempt_count`
- `last_attempt_at`
- `last_error`

## Mutation Rules

1. Save local event changes immediately.
2. Enqueue a mutation in the same local transaction.
3. Retry mutations until the server accepts or returns a permanent validation error.
4. Do not drop failed mutations silently.
5. `client_mutation_id` makes retries idempotent.

## Conflict Policy

Version 1 uses last-write-wins with conflict detection:

- If `base_remote_updated_at` matches the remote row, apply the mutation.
- If the remote row changed after `base_remote_updated_at`, mark local row as `conflict`.
- The first UI can show the newest copy and preserve the local copy in `payload_json`.

## Server Contract

Native clients should eventually call a Supabase Edge Function instead of writing directly to tables. The function should:

1. Verify the user's JWT.
2. Read the authenticated user ID from Supabase Auth, not the request body.
3. Validate mutation payloads.
4. Confirm the user owns the target calendar/event.
5. Apply mutations transactionally.
6. Return accepted mutation IDs and changed remote events since `last_sync_cursor`.

## Initial API Shape

```json
{
  "client_id": "device uuid",
  "last_sync_cursor": "2026-05-18T00:00:00.000Z",
  "mutations": [
    {
      "client_mutation_id": "uuid",
      "entity_type": "event",
      "operation": "update",
      "entity_remote_id": "uuid",
      "base_remote_updated_at": "2026-05-18T00:00:00.000Z",
      "payload": {
        "title": "Research brief",
        "starts_at": "2026-05-19T17:00:00.000Z",
        "ends_at": "2026-05-19T18:00:00.000Z"
      }
    }
  ]
}
```

## Response Shape

```json
{
  "server_time": "2026-05-18T00:01:00.000Z",
  "next_sync_cursor": "2026-05-18T00:01:00.000Z",
  "accepted_mutation_ids": ["uuid"],
  "conflicts": [],
  "events": []
}
```
