namespace TregCalendar.Core;

public sealed record LocalCalendarEvent
{
    public Guid LocalId { get; init; } = Guid.NewGuid();

    public Guid? RemoteId { get; init; }

    public Guid CalendarId { get; init; }

    public Guid? OwnerId { get; init; }

    public string Title { get; init; } = string.Empty;

    public string? DescriptionHtml { get; init; }

    public string? Location { get; init; }

    public DateTimeOffset? StartsAt { get; init; }

    public DateTimeOffset? EndsAt { get; init; }

    public DateTimeOffset? DueAt { get; init; }

    public bool AllDay { get; init; }

    public DateOnly? AllDayDate { get; init; }

    public string? Rrule { get; init; }

    public string? CourseName { get; init; }

    public string? CourseColor { get; init; }

    public string Status { get; init; } = "active";

    public DateTimeOffset? DeletedAt { get; init; }

    public DateTimeOffset? RemoteUpdatedAt { get; init; }

    public DateTimeOffset LocalUpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    public EventSyncState SyncState { get; init; } = EventSyncState.Pending;
}
