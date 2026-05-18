namespace TregCalendar.Auth;

public sealed record AuthSession
{
    public required string AccessToken { get; init; }

    public required string RefreshToken { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public required string UserId { get; init; }

    public required string Email { get; init; }

    public bool IsExpiringSoon(DateTimeOffset now)
    {
        return ExpiresAt <= now.AddMinutes(5);
    }
}
