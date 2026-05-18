namespace TregCalendar.Core;

public enum PendingMutationOperation
{
    Create,
    Update,
    Delete
}

public sealed record PendingMutation
{
    public Guid ClientMutationId { get; init; } = Guid.NewGuid();

    public string EntityType { get; init; } = "event";

    public Guid EntityLocalId { get; init; }

    public Guid? EntityRemoteId { get; init; }

    public PendingMutationOperation Operation { get; init; }

    public DateTimeOffset? BaseRemoteUpdatedAt { get; init; }

    public string PayloadJson { get; init; } = "{}";

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public int AttemptCount { get; init; }

    public DateTimeOffset? LastAttemptAt { get; init; }

    public string? LastError { get; init; }
}
