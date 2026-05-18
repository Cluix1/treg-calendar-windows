namespace TregCalendar.Core;

public sealed record AcceptedMutation
{
    public Guid ClientMutationId { get; init; }

    public Guid? RemoteId { get; init; }

    public string Status { get; init; } = "accepted";
}
