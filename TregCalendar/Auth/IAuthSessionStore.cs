namespace TregCalendar.Auth;

public interface IAuthSessionStore
{
    Task<AuthSession?> GetSessionAsync(CancellationToken cancellationToken = default);

    Task SaveSessionAsync(AuthSession session, CancellationToken cancellationToken = default);

    Task ClearSessionAsync(CancellationToken cancellationToken = default);
}
