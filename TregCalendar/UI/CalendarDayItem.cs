using TregCalendar.Core;

namespace TregCalendar.UI;

public sealed record CalendarDayItem
{
    public required DateOnly Date { get; init; }

    public required string WeekdayText { get; init; }

    public required string DayText { get; init; }

    public required string EventSummaryText { get; init; }

    public required string ToneText { get; init; }

    public IReadOnlyList<LocalCalendarEvent> Events { get; init; } = [];

    public static CalendarDayItem FromDate(
        DateOnly date,
        DateOnly visibleDate,
        DateOnly focusedMonth,
        IReadOnlyList<LocalCalendarEvent> events)
    {
        return new CalendarDayItem
        {
            Date = date,
            WeekdayText = date.ToDateTime(TimeOnly.MinValue).ToString("ddd"),
            DayText = date.Day.ToString(),
            EventSummaryText = FormatEventSummary(events),
            ToneText = FormatTone(date, visibleDate, focusedMonth),
            Events = events
        };
    }

    private static string FormatEventSummary(IReadOnlyList<LocalCalendarEvent> events)
    {
        if (events.Count == 0)
        {
            return "No events";
        }

        if (events.Count == 1)
        {
            return events[0].Title;
        }

        return $"{events.Count} events";
    }

    private static string FormatTone(DateOnly date, DateOnly visibleDate, DateOnly focusedMonth)
    {
        if (date == visibleDate)
        {
            return "Selected";
        }

        if (date == DateOnly.FromDateTime(DateTime.Now))
        {
            return "Today";
        }

        return date.Month == focusedMonth.Month && date.Year == focusedMonth.Year ? "" : "Other month";
    }
}
