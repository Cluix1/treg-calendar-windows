using Microsoft.Data.Sqlite;

namespace TregCalendar.Data;

public sealed class LocalCalendarDatabase
{
    private const int CurrentSchemaVersion = 1;

    public LocalCalendarDatabase(string? databasePath = null)
    {
        DatabasePath = databasePath ?? GetDefaultDatabasePath();
    }

    public string DatabasePath { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken);

        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS schema_metadata (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """,
            cancellationToken);

        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS local_events (
                local_id TEXT PRIMARY KEY,
                id TEXT UNIQUE,
                calendar_id TEXT NOT NULL,
                owner_id TEXT,
                title TEXT NOT NULL,
                description_html TEXT,
                location TEXT,
                starts_at TEXT,
                ends_at TEXT,
                due_at TEXT,
                all_day INTEGER NOT NULL DEFAULT 0,
                all_day_date TEXT,
                rrule TEXT,
                course_name TEXT,
                course_color TEXT,
                status TEXT NOT NULL DEFAULT 'active',
                deleted_at TEXT,
                remote_updated_at TEXT,
                local_updated_at TEXT NOT NULL,
                sync_state TEXT NOT NULL CHECK (sync_state IN ('synced', 'pending', 'conflict', 'deleted'))
            );
            """,
            cancellationToken);

        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS pending_mutations (
                client_mutation_id TEXT PRIMARY KEY,
                entity_type TEXT NOT NULL CHECK (entity_type = 'event'),
                entity_local_id TEXT NOT NULL,
                entity_remote_id TEXT,
                operation TEXT NOT NULL CHECK (operation IN ('create', 'update', 'delete')),
                base_remote_updated_at TEXT,
                payload_json TEXT NOT NULL,
                created_at TEXT NOT NULL,
                attempt_count INTEGER NOT NULL DEFAULT 0,
                last_attempt_at TEXT,
                last_error TEXT,
                FOREIGN KEY (entity_local_id) REFERENCES local_events(local_id) ON DELETE CASCADE
            );
            """,
            cancellationToken);

        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS sync_state (
                key TEXT PRIMARY KEY,
                value TEXT,
                updated_at TEXT NOT NULL
            );
            """,
            cancellationToken);

        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_local_events_remote_id ON local_events(id);", cancellationToken);
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_local_events_calendar_id ON local_events(calendar_id);", cancellationToken);
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_local_events_sync_state ON local_events(sync_state);", cancellationToken);
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS idx_pending_mutations_created_at ON pending_mutations(created_at);", cancellationToken);

        await UpsertSchemaVersionAsync(connection, cancellationToken);
    }

    public SqliteConnection CreateConnection()
    {
        return new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString());
    }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;", cancellationToken);
        return connection;
    }

    public static string GetDefaultDatabasePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Treg",
            "Calendar",
            "treg-calendar.db");
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertSchemaVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO schema_metadata (key, value, updated_at)
            VALUES ('schema_version', $schemaVersion, $updatedAt)
            ON CONFLICT(key) DO UPDATE SET
                value = excluded.value,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$schemaVersion", CurrentSchemaVersion.ToString());
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
