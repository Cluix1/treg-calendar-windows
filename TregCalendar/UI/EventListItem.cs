using TregCalendar.Core;

namespace TregCalendar.UI;

public sealed record EventListItem
{
    public required LocalCalendarEvent Event { get; init; }

    public required string Title { get; init; }

    public required string DateText { get; init; }

    public required string TimeText { get; init; }

    public required string DetailText { get; init; }

    public required string SyncStateText { get; init; }

    public static EventListItem FromEvent(LocalCalendarEvent calendarEvent)
    {
        var startsAt = calendarEvent.StartsAt ?? calendarEvent.DueAt ?? calendarEvent.EndsAt;
        var dateText = startsAt?.ToLocalTime().ToString("ddd, MMM d") ?? "No date";
        var timeText = calendarEvent.AllDay
            ? "All day"
            : FormatTimeRange(calendarEvent.StartsAt, calendarEvent.EndsAt);

        return new EventListItem
        {
            Event = calendarEvent,
            Title = string.IsNullOrWhiteSpace(calendarEvent.Title) ? "Untitled event" : calendarEvent.Title,
            DateText = dateText,
            TimeText = timeText,
            DetailText = BuildDetail(calendarEvent),
            SyncStateText = calendarEvent.SyncState.ToString()
        };
    }

    private static string FormatTimeRange(DateTimeOffset? startsAt, DateTimeOffset? endsAt)
    {
        if (startsAt is null && endsAt is null)
        {
            return "No time";
        }

        if (startsAt is null)
        {
            return $"Ends {endsAt!.Value.ToLocalTime():h:mm tt}";
        }

        if (endsAt is null)
        {
            return startsAt.Value.ToLocalTime().ToString("h:mm tt");
        }

        return $"{startsAt.Value.ToLocalTime():h:mm tt} - {endsAt.Value.ToLocalTime():h:mm tt}";
    }

    private static string BuildDetail(LocalCalendarEvent calendarEvent)
    {
        var parts = new[]
        {
            calendarEvent.CourseName,
            calendarEvent.Location,
            calendarEvent.Status is "active" ? null : calendarEvent.Status
        }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!)
            .ToArray();

        return parts.Length == 0 ? "Treg Calendar" : string.Join(" - ", parts);
    }
}
