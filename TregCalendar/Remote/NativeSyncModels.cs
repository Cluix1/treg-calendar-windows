using System.Text.Json;
using System.Text.Json.Serialization;
using TregCalendar.Core;

namespace TregCalendar.Remote;

internal sealed record NativeSyncRequestDto
{
    [JsonPropertyName("client_id")]
    public required string ClientId { get; init; }

    [JsonPropertyName("last_sync_cursor")]
    public string? LastSyncCursor { get; init; }

    [JsonPropertyName("mutations")]
    public required IReadOnlyList<NativeMutationDto> Mutations { get; init; }
}

internal sealed record NativeMutationDto
{
    [JsonPropertyName("client_mutation_id")]
    public required string ClientMutationId { get; init; }

    [JsonPropertyName("entity_type")]
    public required string EntityType { get; init; }

    [JsonPropertyName("entity_remote_id")]
    public string? EntityRemoteId { get; init; }

    [JsonPropertyName("operation")]
    public required string Operation { get; init; }

    [JsonPropertyName("base_remote_updated_at")]
    public string? BaseRemoteUpdatedAt { get; init; }

    [JsonPropertyName("payload")]
    public required JsonElement Payload { get; init; }
}

public sealed record NativeSyncResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("server_time")]
    public string? ServerTime { get; init; }

    [JsonPropertyName("next_sync_cursor")]
    public string? NextSyncCursor { get; init; }

    [JsonPropertyName("accepted_mutations")]
    public IReadOnlyList<NativeAcceptedMutationDto> AcceptedMutations { get; init; } = [];

    [JsonPropertyName("conflicts")]
    public IReadOnlyList<NativeConflictDto> Conflicts { get; init; } = [];

    [JsonPropertyName("errors")]
    public IReadOnlyList<NativeSyncErrorDto> Errors { get; init; } = [];

    [JsonPropertyName("events")]
    public IReadOnlyList<NativeEventDto> Events { get; init; } = [];
}

public sealed record NativeAcceptedMutationDto
{
    [JsonPropertyName("client_mutation_id")]
    public string? ClientMutationId { get; init; }

    [JsonPropertyName("remote_id")]
    public string? RemoteId { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    public AcceptedMutation? ToAcceptedMutation()
    {
        if (!Guid.TryParse(ClientMutationId, out var clientMutationId))
        {
            return null;
        }

        return new AcceptedMutation
        {
            ClientMutationId = clientMutationId,
            RemoteId = Guid.TryParse(RemoteId, out var remoteId) ? remoteId : null,
            Status = Status ?? "accepted"
        };
    }
}

public sealed record NativeConflictDto
{
    [JsonPropertyName("client_mutation_id")]
    public string? ClientMutationId { get; init; }

    [JsonPropertyName("remote_id")]
    public string? RemoteId { get; init; }

    [JsonPropertyName("remote_updated_at")]
    public string? RemoteUpdatedAt { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

public sealed record NativeSyncErrorDto
{
    [JsonPropertyName("client_mutation_id")]
    public string? ClientMutationId { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}

public sealed record NativeEventDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("calendar_id")]
    public string? CalendarId { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("description_html")]
    public string? DescriptionHtml { get; init; }

    [JsonPropertyName("location")]
    public string? Location { get; init; }

    [JsonPropertyName("starts_at")]
    public string? StartsAt { get; init; }

    [JsonPropertyName("ends_at")]
    public string? EndsAt { get; init; }

    [JsonPropertyName("due_at")]
    public string? DueAt { get; init; }

    [JsonPropertyName("all_day")]
    public bool AllDay { get; init; }

    [JsonPropertyName("all_day_date")]
    public string? AllDayDate { get; init; }

    [JsonPropertyName("rrule")]
    public string? Rrule { get; init; }

    [JsonPropertyName("course_name")]
    public string? CourseName { get; init; }

    [JsonPropertyName("course_color")]
    public string? CourseColor { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; init; }

    [JsonPropertyName("deleted_at")]
    public string? DeletedAt { get; init; }

    public LocalCalendarEvent? ToLocalEvent()
    {
        if (!Guid.TryParse(Id, out var remoteId) || !Guid.TryParse(CalendarId, out var calendarId))
        {
            return null;
        }

        return new LocalCalendarEvent
        {
            LocalId = remoteId,
            RemoteId = remoteId,
            CalendarId = calendarId,
            Title = Title ?? string.Empty,
            DescriptionHtml = DescriptionHtml,
            Location = Location,
            StartsAt = ParseDate(StartsAt),
            EndsAt = ParseDate(EndsAt),
            DueAt = ParseDate(DueAt),
            AllDay = AllDay,
            AllDayDate = DateOnly.TryParse(AllDayDate, out var allDayDate) ? allDayDate : null,
            Rrule = Rrule,
            CourseName = CourseName,
            CourseColor = CourseColor,
            Status = Status ?? "active",
            DeletedAt = ParseDate(DeletedAt),
            RemoteUpdatedAt = ParseDate(UpdatedAt),
            SyncState = DeletedAt is null ? EventSyncState.Synced : EventSyncState.Deleted
        };
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }
}
