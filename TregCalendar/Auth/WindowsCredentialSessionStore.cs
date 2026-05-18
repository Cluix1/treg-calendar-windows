using Windows.Security.Credentials;

namespace TregCalendar.Auth;

public sealed class WindowsCredentialSessionStore : IAuthSessionStore
{
    private const string Resource = "TregCalendar.SupabaseAuth";
    private const string AccessTokenKey = "access_token";
    private const string RefreshTokenKey = "refresh_token";
    private const string ExpiresAtKey = "expires_at";
    private const string UserIdKey = "user_id";
    private const string EmailKey = "email";

    private readonly PasswordVault _vault = new();

    public Task<AuthSession?> GetSessionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var accessToken = ReadSecret(AccessTokenKey);
        var refreshToken = ReadSecret(RefreshTokenKey);
        var expiresAtText = ReadSecret(ExpiresAtKey);
        var userId = ReadSecret(UserIdKey);
        var email = ReadSecret(EmailKey);

        if (string.IsNullOrWhiteSpace(accessToken)
            || string.IsNullOrWhiteSpace(refreshToken)
            || string.IsNullOrWhiteSpace(expiresAtText)
            || string.IsNullOrWhiteSpace(userId)
            || string.IsNullOrWhiteSpace(email)
            || !DateTimeOffset.TryParse(expiresAtText, out var expiresAt))
        {
            return Task.FromResult<AuthSession?>(null);
        }

        return Task.FromResult<AuthSession?>(new AuthSession
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = expiresAt,
            UserId = userId,
            Email = email
        });
    }

    public Task SaveSessionAsync(AuthSession session, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        WriteSecret(AccessTokenKey, session.AccessToken);
        WriteSecret(RefreshTokenKey, session.RefreshToken);
        WriteSecret(ExpiresAtKey, session.ExpiresAt.ToUniversalTime().ToString("O"));
        WriteSecret(UserIdKey, session.UserId);
        WriteSecret(EmailKey, session.Email);

        return Task.CompletedTask;
    }

    public Task ClearSessionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        RemoveSecret(AccessTokenKey);
        RemoveSecret(RefreshTokenKey);
        RemoveSecret(ExpiresAtKey);
        RemoveSecret(UserIdKey);
        RemoveSecret(EmailKey);

        return Task.CompletedTask;
    }

    private string? ReadSecret(string key)
    {
        try
        {
            var credential = _vault.Retrieve(Resource, key);
            credential.RetrievePassword();
            return credential.Password;
        }
        catch
        {
            return null;
        }
    }

    private void WriteSecret(string key, string value)
    {
        RemoveSecret(key);
        _vault.Add(new PasswordCredential(Resource, key, value));
    }

    private void RemoveSecret(string key)
    {
        try
        {
            _vault.Remove(_vault.Retrieve(Resource, key));
        }
        catch
        {
            // PasswordVault throws when the credential is not present.
        }
    }
}
