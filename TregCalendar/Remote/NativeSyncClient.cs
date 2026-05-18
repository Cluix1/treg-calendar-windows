using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TregCalendar.Core;

namespace TregCalendar.Remote;

public sealed class NativeSyncClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly IAccessTokenProvider _accessTokenProvider;
    private readonly NativeSyncClientOptions _options;

    public NativeSyncClient(
        HttpClient httpClient,
        IAccessTokenProvider accessTokenProvider,
        NativeSyncClientOptions? options = null)
    {
        _httpClient = httpClient;
        _accessTokenProvider = accessTokenProvider;
        _options = options ?? new NativeSyncClientOptions();
    }

    public async Task<NativeSyncResponse> SyncAsync(
        Guid clientId,
        string? lastSyncCursor,
        IReadOnlyList<PendingMutation> mutations,
        CancellationToken cancellationToken = default)
    {
        var accessToken = await _accessTokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException("Sign in before syncing.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.FunctionUri)
        {
            Content = JsonContent.Create(
                new NativeSyncRequestDto
                {
                    ClientId = clientId.ToString(),
                    LastSyncCursor = lastSyncCursor,
                    Mutations = mutations.Select(ToDto).ToArray()
                },
                options: JsonOptions)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Native sync failed with {(int)response.StatusCode}: {body}");
        }

        return JsonSerializer.Deserialize<NativeSyncResponse>(body, JsonOptions)
            ?? throw new InvalidOperationException("Native sync returned an empty response.");
    }

    private static NativeMutationDto ToDto(PendingMutation mutation)
    {
        return new NativeMutationDto
        {
            ClientMutationId = mutation.ClientMutationId.ToString(),
            EntityType = mutation.EntityType,
            EntityRemoteId = mutation.EntityRemoteId?.ToString(),
            Operation = FormatOperation(mutation.Operation),
            BaseRemoteUpdatedAt = mutation.BaseRemoteUpdatedAt?.ToUniversalTime().ToString("O"),
            Payload = JsonSerializer.Deserialize<JsonElement>(mutation.PayloadJson)
        };
    }

    private static string FormatOperation(PendingMutationOperation operation)
    {
        return operation switch
        {
            PendingMutationOperation.Create => "create",
            PendingMutationOperation.Update => "update",
            PendingMutationOperation.Delete => "delete",
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };
    }
}
