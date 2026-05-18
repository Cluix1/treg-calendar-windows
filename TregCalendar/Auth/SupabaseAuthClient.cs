using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using TregCalendar.Remote;

namespace TregCalendar.Auth;

public sealed class SupabaseAuthClient : IAccessTokenProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly IAuthSessionStore _sessionStore;
    private readonly SupabaseAuthOptions _options;

    public SupabaseAuthClient(
        HttpClient httpClient,
        IAuthSessionStore sessionStore,
        SupabaseAuthOptions? options = null)
    {
        _httpClient = httpClient;
        _sessionStore = sessionStore;
        _options = options ?? new SupabaseAuthOptions();
    }

    public async Task<AuthSession?> GetCurrentSessionAsync(CancellationToken cancellationToken = default)
    {
        return await _sessionStore.GetSessionAsync(cancellationToken);
    }

    public async Task<AuthSession> SignInWithPasswordAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var response = await SendAuthRequestAsync(
            HttpMethod.Post,
            "auth/v1/token?grant_type=password",
            new
            {
                email = email.Trim().ToLowerInvariant(),
                password
            },
            bearerToken: null,
            cancellationToken);

        var session = ToSession(response);
        await _sessionStore.SaveSessionAsync(session, cancellationToken);
        return session;
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var session = await _sessionStore.GetSessionAsync(cancellationToken);
        if (session is not null)
        {
            using var request = CreateRequest(HttpMethod.Post, "auth/v1/logout", bearerToken: session.AccessToken);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.Unauthorized)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException($"Supabase logout failed with {(int)response.StatusCode}: {body}");
            }
        }

        await _sessionStore.ClearSessionAsync(cancellationToken);
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var session = await _sessionStore.GetSessionAsync(cancellationToken);
        if (session is null)
        {
            return null;
        }

        if (!session.IsExpiringSoon(DateTimeOffset.UtcNow))
        {
            return session.AccessToken;
        }

        var refreshed = await RefreshSessionAsync(session.RefreshToken, cancellationToken);
        return refreshed.AccessToken;
    }

    private async Task<AuthSession> RefreshSessionAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        var response = await SendAuthRequestAsync(
            HttpMethod.Post,
            "auth/v1/token?grant_type=refresh_token",
            new { refresh_token = refreshToken },
            bearerToken: null,
            cancellationToken);

        var session = ToSession(response);
        await _sessionStore.SaveSessionAsync(session, cancellationToken);
        return session;
    }

    private async Task<SupabaseAuthResponse> SendAuthRequestAsync(
        HttpMethod method,
        string relativePath,
        object body,
        string? bearerToken,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, relativePath, bearerToken);
        request.Content = JsonContent.Create(body, options: JsonOptions);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Supabase auth failed with {(int)response.StatusCode}: {responseBody}");
        }

        return JsonSerializer.Deserialize<SupabaseAuthResponse>(responseBody, JsonOptions)
            ?? throw new InvalidOperationException("Supabase auth returned an empty response.");
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath, string? bearerToken)
    {
        var request = new HttpRequestMessage(method, new Uri(_options.SupabaseUrl, relativePath));
        request.Headers.Add("apikey", _options.PublishableKey);
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        return request;
    }

    private void EnsureConfigured()
    {
        if (!_options.HasPublishableKey)
        {
            throw new InvalidOperationException("Set TREG_SUPABASE_PUBLISHABLE_KEY before signing in.");
        }
    }

    private static AuthSession ToSession(SupabaseAuthResponse response)
    {
        if (string.IsNullOrWhiteSpace(response.AccessToken)
            || string.IsNullOrWhiteSpace(response.RefreshToken)
            || response.User is null
            || string.IsNullOrWhiteSpace(response.User.Id))
        {
            throw new InvalidOperationException("Supabase auth response was missing session data.");
        }

        return new AuthSession
        {
            AccessToken = response.AccessToken,
            RefreshToken = response.RefreshToken,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, response.ExpiresIn)),
            UserId = response.User.Id,
            Email = response.User.Email ?? "unknown@example.com"
        };
    }
}

internal sealed record SupabaseAuthResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; init; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; init; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }

    [JsonPropertyName("user")]
    public SupabaseAuthUser? User { get; init; }
}

internal sealed record SupabaseAuthUser
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }
}
