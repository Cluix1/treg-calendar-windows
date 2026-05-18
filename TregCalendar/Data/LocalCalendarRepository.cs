using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TregCalendar.Core;

namespace TregCalendar.Data;

public sealed class LocalCalendarRepository
{
    private const int DefaultMutationBatchSize = 100;
    private readonly LocalCalendarDatabase _database;

    public LocalCalendarRepository(LocalCalendarDatabase database)
    {
        _database = database;
    }

    public async Task SaveEventWithMutationAsync(
        LocalCalendarEvent calendarEvent,
        PendingMutationOperation operation,
        string payloadJson,
        DateTimeOffset? baseRemoteUpdatedAt = null,
        CancellationToken cancellationToken = default)
    {
        EnsureValidPayload(payloadJson);

        var now = DateTimeOffset.UtcNow;
        var syncState = operation == PendingMutationOperation.Delete
            ? EventSyncState.Deleted
            : EventSyncState.Pending;
        var deletedAt = operation == PendingMutationOperation.Delete
            ? calendarEvent.DeletedAt ?? now
            : calendarEvent.DeletedAt;

        var eventToSave = calendarEvent with
        {
            LocalUpdatedAt = now,
            DeletedAt = deletedAt,
            SyncState = syncState
        };

        var mutation = new PendingMutation
        {
            EntityLocalId = eventToSave.LocalId,
            EntityRemoteId = eventToSave.RemoteId,
            Operation = operation,
            BaseRemoteUpdatedAt = baseRemoteUpdatedAt ?? eventToSave.RemoteUpdatedAt,
            PayloadJson = payloadJson,
            CreatedAt = now
        };

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await UpsertEventAsync(connection, transaction, eventToSave, cancellationToken);
        await InsertPendingMutationAsync(connection, transaction, mutation, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LocalCalendarEvent>> GetEventsAsync(
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                local_id,
                id,
                calendar_id,
                owner_id,
                title,
                description_html,
                location,
                starts_at,
                ends_at,
                due_at,
                all_day,
                all_day_date,
                rrule,
                course_name,
                course_color,
                status,
                deleted_at,
                remote_updated_at,
                local_updated_at,
                sync_state
            FROM local_events
            WHERE $includeDeleted = 1 OR sync_state <> 'deleted'
            ORDER BY COALESCE(starts_at, due_at, local_updated_at), title;
            """;
        command.Parameters.AddWithValue("$includeDeleted", includeDeleted ? 1 : 0);

        var events = new List<LocalCalendarEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(ReadEvent(reader));
        }

        return events;
    }

    public async Task<IReadOnlyList<PendingMutation>> GetPendingMutationsAsync(
        int limit = DefaultMutationBatchSize,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than zero.");
        }

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                client_mutation_id,
                entity_type,
                entity_local_id,
                entity_remote_id,
                operation,
                base_remote_updated_at,
                payload_json,
                created_at,
                attempt_count,
                last_attempt_at,
                last_error
            FROM pending_mutations
            ORDER BY created_at
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        var mutations = new List<PendingMutation>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            mutations.Add(ReadMutation(reader));
        }

        return mutations;
    }

    public async Task<int> CountPendingMutationsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pending_mutations;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task MarkMutationsAcceptedAsync(
        IEnumerable<Guid> clientMutationIds,
        CancellationToken cancellationToken = default)
    {
        var acceptedMutations = clientMutationIds
            .Distinct()
            .Select(clientMutationId => new AcceptedMutation { ClientMutationId = clientMutationId })
            .ToArray();

        await MarkMutationsAcceptedAsync(acceptedMutations, cancellationToken);
    }

    public async Task MarkMutationsAcceptedAsync(
        IEnumerable<AcceptedMutation> acceptedMutations,
        CancellationToken cancellationToken = default)
    {
        var mutations = acceptedMutations
            .GroupBy(mutation => mutation.ClientMutationId)
            .Select(group => group.First())
            .ToArray();

        if (mutations.Length == 0)
        {
            return;
        }

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        foreach (var acceptedMutation in mutations)
        {
            Guid? entityLocalId = null;

            await using (var lookup = connection.CreateCommand())
            {
                lookup.Transaction = (SqliteTransaction)transaction;
                lookup.CommandText = "SELECT entity_local_id FROM pending_mutations WHERE client_mutation_id = $clientMutationId;";
                lookup.Parameters.AddWithValue("$clientMutationId", acceptedMutation.ClientMutationId.ToString());

                var value = await lookup.ExecuteScalarAsync(cancellationToken);
                if (value is string rawId && Guid.TryParse(rawId, out var parsedId))
                {
                    entityLocalId = parsedId;
                }
            }

            await using (var delete = connection.CreateCommand())
            {
                delete.Transaction = (SqliteTransaction)transaction;
                delete.CommandText = "DELETE FROM pending_mutations WHERE client_mutation_id = $clientMutationId;";
                delete.Parameters.AddWithValue("$clientMutationId", acceptedMutation.ClientMutationId.ToString());
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }

            if (entityLocalId is null || await HasPendingMutationAsync(connection, transaction, entityLocalId.Value, cancellationToken))
            {
                continue;
            }

            await using var update = connection.CreateCommand();
            update.Transaction = (SqliteTransaction)transaction;
            update.CommandText =
                """
                UPDATE local_events
                SET
                    id = COALESCE($remoteId, id),
                    sync_state = CASE WHEN deleted_at IS NULL THEN 'synced' ELSE 'deleted' END
                WHERE local_id = $localId;
                """;
            update.Parameters.AddWithValue("$localId", entityLocalId.Value.ToString());
            update.Parameters.AddWithValue("$remoteId", ToDbValue(acceptedMutation.RemoteId));
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RecordMutationFailureAsync(
        Guid clientMutationId,
        string error,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE pending_mutations
            SET
                attempt_count = attempt_count + 1,
                last_attempt_at = $lastAttemptAt,
                last_error = $lastError
            WHERE client_mutation_id = $clientMutationId;
            """;
        command.Parameters.AddWithValue("$clientMutationId", clientMutationId.ToString());
        command.Parameters.AddWithValue("$lastAttemptAt", FormatDate(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$lastError", error);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ApplyRemoteEventAsync(
        LocalCalendarEvent remoteEvent,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var localId = await ResolveLocalIdForRemoteEventAsync(connection, transaction, remoteEvent, cancellationToken);

        if (await HasPendingMutationAsync(connection, transaction, localId, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var syncedEvent = remoteEvent with
        {
            LocalId = localId,
            LocalUpdatedAt = DateTimeOffset.UtcNow,
            SyncState = remoteEvent.DeletedAt is null ? EventSyncState.Synced : EventSyncState.Deleted
        };

        await UpsertEventAsync(connection, transaction, syncedEvent, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<string?> GetSyncStateValueAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM sync_state WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    public async Task SetSyncStateValueAsync(
        string key,
        string? value,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO sync_state (key, value, updated_at)
            VALUES ($key, $value, $updatedAt)
            ON CONFLICT(key) DO UPDATE SET
                value = excluded.value,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value is null ? DBNull.Value : value);
        command.Parameters.AddWithValue("$updatedAt", FormatDate(DateTimeOffset.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertEventAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        LocalCalendarEvent calendarEvent,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            INSERT INTO local_events (
                local_id,
                id,
                calendar_id,
                owner_id,
                title,
                description_html,
                location,
                starts_at,
                ends_at,
                due_at,
                all_day,
                all_day_date,
                rrule,
                course_name,
                course_color,
                status,
                deleted_at,
                remote_updated_at,
                local_updated_at,
                sync_state
            )
            VALUES (
                $localId,
                $id,
                $calendarId,
                $ownerId,
                $title,
                $descriptionHtml,
                $location,
                $startsAt,
                $endsAt,
                $dueAt,
                $allDay,
                $allDayDate,
                $rrule,
                $courseName,
                $courseColor,
                $status,
                $deletedAt,
                $remoteUpdatedAt,
                $localUpdatedAt,
                $syncState
            )
            ON CONFLICT(local_id) DO UPDATE SET
                id = excluded.id,
                calendar_id = excluded.calendar_id,
                owner_id = excluded.owner_id,
                title = excluded.title,
                description_html = excluded.description_html,
                location = excluded.location,
                starts_at = excluded.starts_at,
                ends_at = excluded.ends_at,
                due_at = excluded.due_at,
                all_day = excluded.all_day,
                all_day_date = excluded.all_day_date,
                rrule = excluded.rrule,
                course_name = excluded.course_name,
                course_color = excluded.course_color,
                status = excluded.status,
                deleted_at = excluded.deleted_at,
                remote_updated_at = excluded.remote_updated_at,
                local_updated_at = excluded.local_updated_at,
                sync_state = excluded.sync_state;
            """;
        AddEventParameters(command, calendarEvent);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertPendingMutationAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        PendingMutation mutation,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            INSERT INTO pending_mutations (
                client_mutation_id,
                entity_type,
                entity_local_id,
                entity_remote_id,
                operation,
                base_remote_updated_at,
                payload_json,
                created_at,
                attempt_count,
                last_attempt_at,
                last_error
            )
            VALUES (
                $clientMutationId,
                $entityType,
                $entityLocalId,
                $entityRemoteId,
                $operation,
                $baseRemoteUpdatedAt,
                $payloadJson,
                $createdAt,
                $attemptCount,
                $lastAttemptAt,
                $lastError
            );
            """;
        AddMutationParameters(command, mutation);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> HasPendingMutationAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        Guid entityLocalId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "SELECT 1 FROM pending_mutations WHERE entity_local_id = $entityLocalId LIMIT 1;";
        command.Parameters.AddWithValue("$entityLocalId", entityLocalId.ToString());
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task<Guid> ResolveLocalIdForRemoteEventAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        LocalCalendarEvent remoteEvent,
        CancellationToken cancellationToken)
    {
        if (remoteEvent.RemoteId is null)
        {
            return remoteEvent.LocalId;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "SELECT local_id FROM local_events WHERE id = $remoteId LIMIT 1;";
        command.Parameters.AddWithValue("$remoteId", remoteEvent.RemoteId.Value.ToString());

        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is string rawId && Guid.TryParse(rawId, out var parsedId)
            ? parsedId
            : remoteEvent.LocalId;
    }

    private static void AddEventParameters(SqliteCommand command, LocalCalendarEvent calendarEvent)
    {
        command.Parameters.AddWithValue("$localId", calendarEvent.LocalId.ToString());
        command.Parameters.AddWithValue("$id", ToDbValue(calendarEvent.RemoteId));
        command.Parameters.AddWithValue("$calendarId", calendarEvent.CalendarId.ToString());
        command.Parameters.AddWithValue("$ownerId", ToDbValue(calendarEvent.OwnerId));
        command.Parameters.AddWithValue("$title", calendarEvent.Title);
        command.Parameters.AddWithValue("$descriptionHtml", ToDbValue(calendarEvent.DescriptionHtml));
        command.Parameters.AddWithValue("$location", ToDbValue(calendarEvent.Location));
        command.Parameters.AddWithValue("$startsAt", ToDbValue(calendarEvent.StartsAt));
        command.Parameters.AddWithValue("$endsAt", ToDbValue(calendarEvent.EndsAt));
        command.Parameters.AddWithValue("$dueAt", ToDbValue(calendarEvent.DueAt));
        command.Parameters.AddWithValue("$allDay", calendarEvent.AllDay ? 1 : 0);
        command.Parameters.AddWithValue("$allDayDate", calendarEvent.AllDayDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$rrule", ToDbValue(calendarEvent.Rrule));
        command.Parameters.AddWithValue("$courseName", ToDbValue(calendarEvent.CourseName));
        command.Parameters.AddWithValue("$courseColor", ToDbValue(calendarEvent.CourseColor));
        command.Parameters.AddWithValue("$status", calendarEvent.Status);
        command.Parameters.AddWithValue("$deletedAt", ToDbValue(calendarEvent.DeletedAt));
        command.Parameters.AddWithValue("$remoteUpdatedAt", ToDbValue(calendarEvent.RemoteUpdatedAt));
        command.Parameters.AddWithValue("$localUpdatedAt", FormatDate(calendarEvent.LocalUpdatedAt));
        command.Parameters.AddWithValue("$syncState", FormatSyncState(calendarEvent.SyncState));
    }

    private static void AddMutationParameters(SqliteCommand command, PendingMutation mutation)
    {
        command.Parameters.AddWithValue("$clientMutationId", mutation.ClientMutationId.ToString());
        command.Parameters.AddWithValue("$entityType", mutation.EntityType);
        command.Parameters.AddWithValue("$entityLocalId", mutation.EntityLocalId.ToString());
        command.Parameters.AddWithValue("$entityRemoteId", ToDbValue(mutation.EntityRemoteId));
        command.Parameters.AddWithValue("$operation", FormatOperation(mutation.Operation));
        command.Parameters.AddWithValue("$baseRemoteUpdatedAt", ToDbValue(mutation.BaseRemoteUpdatedAt));
        command.Parameters.AddWithValue("$payloadJson", mutation.PayloadJson);
        command.Parameters.AddWithValue("$createdAt", FormatDate(mutation.CreatedAt));
        command.Parameters.AddWithValue("$attemptCount", mutation.AttemptCount);
        command.Parameters.AddWithValue("$lastAttemptAt", ToDbValue(mutation.LastAttemptAt));
        command.Parameters.AddWithValue("$lastError", ToDbValue(mutation.LastError));
    }

    private static LocalCalendarEvent ReadEvent(SqliteDataReader reader)
    {
        return new LocalCalendarEvent
        {
            LocalId = ReadGuid(reader, "local_id") ?? throw new DataMisalignedException("local_id is required."),
            RemoteId = ReadGuid(reader, "id"),
            CalendarId = ReadGuid(reader, "calendar_id") ?? throw new DataMisalignedException("calendar_id is required."),
            OwnerId = ReadGuid(reader, "owner_id"),
            Title = ReadString(reader, "title") ?? string.Empty,
            DescriptionHtml = ReadString(reader, "description_html"),
            Location = ReadString(reader, "location"),
            StartsAt = ReadDate(reader, "starts_at"),
            EndsAt = ReadDate(reader, "ends_at"),
            DueAt = ReadDate(reader, "due_at"),
            AllDay = reader.GetInt32(reader.GetOrdinal("all_day")) == 1,
            AllDayDate = ReadDateOnly(reader, "all_day_date"),
            Rrule = ReadString(reader, "rrule"),
            CourseName = ReadString(reader, "course_name"),
            CourseColor = ReadString(reader, "course_color"),
            Status = ReadString(reader, "status") ?? "active",
            DeletedAt = ReadDate(reader, "deleted_at"),
            RemoteUpdatedAt = ReadDate(reader, "remote_updated_at"),
            LocalUpdatedAt = ReadDate(reader, "local_updated_at") ?? DateTimeOffset.UtcNow,
            SyncState = ParseSyncState(ReadString(reader, "sync_state"))
        };
    }

    private static PendingMutation ReadMutation(SqliteDataReader reader)
    {
        return new PendingMutation
        {
            ClientMutationId = ReadGuid(reader, "client_mutation_id") ?? throw new DataMisalignedException("client_mutation_id is required."),
            EntityType = ReadString(reader, "entity_type") ?? "event",
            EntityLocalId = ReadGuid(reader, "entity_local_id") ?? throw new DataMisalignedException("entity_local_id is required."),
            EntityRemoteId = ReadGuid(reader, "entity_remote_id"),
            Operation = ParseOperation(ReadString(reader, "operation")),
            BaseRemoteUpdatedAt = ReadDate(reader, "base_remote_updated_at"),
            PayloadJson = ReadString(reader, "payload_json") ?? "{}",
            CreatedAt = ReadDate(reader, "created_at") ?? DateTimeOffset.UtcNow,
            AttemptCount = reader.GetInt32(reader.GetOrdinal("attempt_count")),
            LastAttemptAt = ReadDate(reader, "last_attempt_at"),
            LastError = ReadString(reader, "last_error")
        };
    }

    private static void EnsureValidPayload(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("Mutation payload must be a JSON object.", nameof(payloadJson));
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Mutation payload must be valid JSON.", nameof(payloadJson), exception);
        }
    }

    private static object ToDbValue(Guid? value)
    {
        return value?.ToString() ?? (object)DBNull.Value;
    }

    private static object ToDbValue(string? value)
    {
        return value ?? (object)DBNull.Value;
    }

    private static object ToDbValue(DateTimeOffset? value)
    {
        return value is null ? DBNull.Value : FormatDate(value.Value);
    }

    private static string FormatDate(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private static string FormatOperation(PendingMutationOperation operation)
    {
        return operation switch
        {
            PendingMutationOperation.Create => "create",
            PendingMutationOperation.Update => "update",
            PendingMutationOperation.Delete => "delete",
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };
    }

    private static string FormatSyncState(EventSyncState state)
    {
        return state switch
        {
            EventSyncState.Synced => "synced",
            EventSyncState.Pending => "pending",
            EventSyncState.Conflict => "conflict",
            EventSyncState.Deleted => "deleted",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
        };
    }

    private static PendingMutationOperation ParseOperation(string? operation)
    {
        return operation switch
        {
            "create" => PendingMutationOperation.Create,
            "update" => PendingMutationOperation.Update,
            "delete" => PendingMutationOperation.Delete,
            _ => throw new DataMisalignedException($"Unknown mutation operation '{operation}'.")
        };
    }

    private static EventSyncState ParseSyncState(string? state)
    {
        return state switch
        {
            "synced" => EventSyncState.Synced,
            "pending" => EventSyncState.Pending,
            "conflict" => EventSyncState.Conflict,
            "deleted" => EventSyncState.Deleted,
            _ => throw new DataMisalignedException($"Unknown event sync state '{state}'.")
        };
    }

    private static Guid? ReadGuid(SqliteDataReader reader, string columnName)
    {
        var value = ReadString(reader, columnName);
        return Guid.TryParse(value, out var guid) ? guid : null;
    }

    private static string? ReadString(SqliteDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTimeOffset? ReadDate(SqliteDataReader reader, string columnName)
    {
        var value = ReadString(reader, columnName);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }

    private static DateOnly? ReadDateOnly(SqliteDataReader reader, string columnName)
    {
        var value = ReadString(reader, columnName);
        return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }
}
