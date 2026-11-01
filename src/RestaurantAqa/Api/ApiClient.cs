using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace RestaurantAqa.Api;

/// <summary>Result of an HTTP call: the status code and the raw body, with JSON helpers for assertions.</summary>
public sealed record ApiResponse(int Status, string Body, string Description)
{
    public bool IsSuccess => Status is >= 200 and < 300;

    public T Json<T>() =>
        JsonSerializer.Deserialize<T>(Body, ApiClient.JsonOptions)
        ?? throw new InvalidOperationException($"Response body is empty or not {typeof(T).Name}: {Describe()}");

    /// <summary>The message of an error response (<c>error</c> or <c>message</c> field), or an empty string.</summary>
    public string ErrorText
    {
        get
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(Body);
                foreach (string name in new[] { "error", "message", "title" })
                {
                    if (document.RootElement.ValueKind == JsonValueKind.Object &&
                        document.RootElement.TryGetProperty(name, out JsonElement value) &&
                        value.ValueKind == JsonValueKind.String)
                    {
                        return value.GetString() ?? string.Empty;
                    }
                }
            }
            catch (JsonException)
            {
            }

            return string.Empty;
        }
    }

    /// <summary>The <c>errorCode</c> of a typed error response, or an empty string.</summary>
    public string ErrorCode
    {
        get
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(Body);
                return document.RootElement.ValueKind == JsonValueKind.Object &&
                       document.RootElement.TryGetProperty("errorCode", out JsonElement value)
                    ? value.GetString() ?? string.Empty
                    : string.Empty;
            }
            catch (JsonException)
            {
                return string.Empty;
            }
        }
    }

    /// <summary>Text for the failure message of an assertion: which call it was and what came back.</summary>
    public string Describe() => $"{Description} -> HTTP {Status}: {(Body.Length > 600 ? Body[..600] + "..." : Body)}";
}

/// <summary>Thin wrapper over <see cref="HttpClient"/> that never throws on non-2xx statuses.</summary>
public sealed class ApiClient : IDisposable
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public ApiClient(string baseUrl)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl + "/"), Timeout = TimeSpan.FromSeconds(60) };
    }

    public Task<ApiResponse> GetAsync(string path, string? bearer = null) =>
        SendAsync(HttpMethod.Get, path, null, bearer);

    public Task<ApiResponse> PostAsync(string path, object? body, string? bearer = null) =>
        SendAsync(HttpMethod.Post, path, body, bearer);

    public Task<ApiResponse> DeleteAsync(string path, object? body = null, string? bearer = null) =>
        SendAsync(HttpMethod.Delete, path, body, bearer);

    public async Task<ApiResponse> SendAsync(HttpMethod method, string path, object? body, string? bearer)
    {
        string json = body is null ? string.Empty : body as string ?? JsonSerializer.Serialize(body, JsonOptions);
        return await SendRawAsync(method, path, json, bearer);
    }

    /// <summary>Sends an already serialized (possibly malformed) body.</summary>
    public async Task<ApiResponse> SendRawAsync(HttpMethod method, string path, string rawBody, string? bearer)
    {
        // The backend sometimes answers a temporary 5xx (limits of the Lambda and DynamoDB): reading is safe to repeat, writing is not.
        ApiResponse response = await SendOnceAsync(method, path, rawBody, bearer);
        for (int attempt = 1; method == HttpMethod.Get && response.Status >= 500 && attempt <= 3; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(attempt));
            response = await SendOnceAsync(method, path, rawBody, bearer);
        }

        return response;
    }

    private async Task<ApiResponse> SendOnceAsync(HttpMethod method, string path, string rawBody, string? bearer)
    {
        using var request = new HttpRequestMessage(method, path.TrimStart('/'));
        if (!string.IsNullOrEmpty(rawBody))
        {
            request.Content = new StringContent(rawBody, Encoding.UTF8, "application/json");
        }

        if (!string.IsNullOrEmpty(bearer))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        using HttpResponseMessage response = await _http.SendAsync(request);
        string text = await response.Content.ReadAsStringAsync();
        return new ApiResponse((int)response.StatusCode, text, $"{method} /{path.TrimStart('/')}");
    }

    public void Dispose() => _http.Dispose();
}
