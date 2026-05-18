using TregCalendar.Core;
using TregCalendar.Data;
using TregCalendar.Remote;

namespace TregCalendar.Sync;

public sealed class CalendarSyncService
{
    private const string ClientIdKey = "native_client_id";
    private const string LastSyncCursorKey = "last_sync_cursor";
    private readonly LocalCalendarDatabase _database;
    private readonly LocalCalendarRepository _repository;
    private readonly NativeSyncClient _syncClient;

    public CalendarSyncService(
        LocalCalendarDatabase database,
        LocalCalendarRepository repository,
        NativeSyncClient syncClient)
    {
        _database = database;
        _repository = repository;
        _syncClient = syncClient;
    }

    public async Task<CalendarSyncResult> SyncOnceAsync(CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);

        var clientId = await GetOrCreateClientIdAsync(cancellationToken);
        var lastSyncCursor = await _repository.GetSyncStateValueAsync(LastSyncCursorKey, cancellationToken);
        var pendingMutations = await _repository.GetPendingMutationsAsync(cancellationToken: cancellationToken);

        var response = await _syncClient.SyncAsync(clientId, lastSyncCursor, pendingMutations, cancellationToken);

        var acceptedMutations = response.AcceptedMutations
            .Select(mutation => mutation.ToAcceptedMutation())
            .OfType<AcceptedMutation>()
            .ToArray();
        await _repository.MarkMutationsAcceptedAsync(acceptedMutations, cancellationToken);

        foreach (var syncError in response.Errors)
        {
            if (Guid.TryParse(syncError.ClientMutationId, out var clientMutationId))
            {
                await _repository.RecordMutationFailureAsync(
                    clientMutationId,
                    syncError.Error ?? "Native sync rejected the mutation.",
                    cancellationToken);
            }
        }

        foreach (var conflict in response.Conflicts)
        {
            if (Guid.TryParse(conflict.ClientMutationId, out var clientMutationId))
            {
                await _repository.RecordMutationFailureAsync(
                    clientMutationId,
                    conflict.Reason ?? "Remote event changed before this local edit synced.",
                    cancellationToken);
            }
        }

        var appliedEvents = 0;
        foreach (var remoteEvent in response.Events.Select(item => item.ToLocalEvent()).OfType<LocalCalendarEvent>())
        {
            await _repository.ApplyRemoteEventAsync(remoteEvent, cancellationToken);
            appliedEvents++;
        }

        if (!string.IsNullOrWhiteSpace(response.NextSyncCursor))
        {
            await _repository.SetSyncStateValueAsync(LastSyncCursorKey, response.NextSyncCursor, cancellationToken);
        }

        return new CalendarSyncResult
        {
            PendingMutationCount = pendingMutations.Count,
            AcceptedMutationCount = acceptedMutations.Length,
            ErrorCount = response.Errors.Count,
            ConflictCount = response.Conflicts.Count,
            AppliedEventCount = appliedEvents,
            NextSyncCursor = response.NextSyncCursor
        };
    }

    private async Task<Guid> GetOrCreateClientIdAsync(CancellationToken cancellationToken)
    {
        var storedClientId = await _repository.GetSyncStateValueAsync(ClientIdKey, cancellationToken);
        if (Guid.TryParse(storedClientId, out var clientId))
        {
            return clientId;
        }

        clientId = Guid.NewGuid();
        await _repository.SetSyncStateValueAsync(ClientIdKey, clientId.ToString(), cancellationToken);
        return clientId;
    }
}

public sealed record CalendarSyncResult
{
    public int PendingMutationCount { get; init; }

    public int AcceptedMutationCount { get; init; }

    public int ErrorCount { get; init; }

    public int ConflictCount { get; init; }

    public int AppliedEventCount { get; init; }

    public string? NextSyncCursor { get; init; }
}
