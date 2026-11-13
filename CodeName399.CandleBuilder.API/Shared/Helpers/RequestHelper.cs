using CodeName399.CandleBuilder.API.Constants;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeName399.CandleBuilder.API.Helpers;

public class RequestHelper
{
    private sealed class EndpointState
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public DateTime NextAllowedUtc = DateTime.MinValue;
        public DateTime CooldownUntilUtc = DateTime.MinValue;
    }

    private readonly HttpClient _httpClient;
    private readonly ILogger<RequestHelper> _logger;
    private static readonly ConcurrentDictionary<string, EndpointState> EndpointStates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan RateLimitCooldown = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan QuoteRequestSpacing = TimeSpan.FromSeconds(1.1);
    private static readonly TimeSpan HistoricalRequestSpacing = TimeSpan.FromSeconds(1.2);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public RequestHelper(IHttpClientFactory factory, ILogger<RequestHelper> logger)
    {
        _httpClient = factory.CreateClient("AngelRefresh");
        _logger = logger;
    }

    public async Task SendAsync(HttpRequestMessage request)
    {
        _ = await SendCoreAsync(request);
    }

    public Task<string> SendRawAsync(HttpRequestMessage request) => SendCoreAsync(request);

    private async Task<string> SendCoreAsync(HttpRequestMessage request)
    {
        var endpoint = GetRateLimitedEndpoint(request.RequestUri);
        EndpointState? state = null;
        var gateAcquired = false;

        try
        {
            if (endpoint != null)
            {
                state = EndpointStates.GetOrAdd(endpoint.Value.Key, _ => new EndpointState());
                await state.Gate.WaitAsync();
                gateAcquired = true;

                var now = DateTime.UtcNow;
                if (state.CooldownUntilUtc > now)
                {
                    var remaining = state.CooldownUntilUtc - now;
                    var suppressed = $"Access denied because of exceeding access rate. Request locally suppressed; {remaining.TotalSeconds:F0}s cooldown remains.";
                    _logger.LogWarning(
                        "ANGEL API REQUEST SUPPRESSED Endpoint={Endpoint} CooldownRemainingSeconds={RemainingSeconds:F0}",
                        endpoint.Value.Key, remaining.TotalSeconds);
                    throw new AngelApiException(System.Net.HttpStatusCode.Forbidden, suppressed);
                }

                var wait = state.NextAllowedUtc - now;
                if (wait > TimeSpan.Zero)
                {
                    _logger.LogDebug("ANGEL API THROTTLE Endpoint={Endpoint} DelayMs={DelayMs:F0}", endpoint.Value.Key, wait.TotalMilliseconds);
                    await Task.Delay(wait);
                }
            }

            using var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                var rateLimited = response.StatusCode == System.Net.HttpStatusCode.Forbidden
                    && content.Contains("exceeding access rate", StringComparison.OrdinalIgnoreCase);

                if (rateLimited && state != null && endpoint != null)
                {
                    state.CooldownUntilUtc = DateTime.UtcNow.Add(RateLimitCooldown);
                    _logger.LogError(
                        "ANGEL API RATE LIMIT Endpoint={Endpoint} CooldownSeconds={CooldownSeconds}. Further requests to this endpoint will be suppressed.",
                        endpoint.Value.Key, RateLimitCooldown.TotalSeconds);
                }

                _logger.LogError(
                    "HTTP {Method} {Url} failed. Status={StatusCode}. Response={Response}",
                    request.Method, request.RequestUri, (int)response.StatusCode, content);

                throw new AngelApiException(response.StatusCode, content);
            }

            return content;
        }
        catch (AngelApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HTTP {Method} {Url} failed.", request.Method, request.RequestUri);
            throw;
        }
        finally
        {
            if (state != null && gateAcquired)
            {
                // Spacing is measured from completion, not request start, to avoid
                // overlapping calls when the broker responds slowly.
                if (state.CooldownUntilUtc <= DateTime.UtcNow)
                {
                    var spacing = endpoint!.Value.Spacing;
                    state.NextAllowedUtc = DateTime.UtcNow.Add(spacing);
                }
                state.Gate.Release();
            }
        }
    }

    private static (string Key, TimeSpan Spacing)? GetRateLimitedEndpoint(Uri? uri)
    {
        if (uri == null) return null;
        var path = uri.IsAbsoluteUri ? uri.AbsolutePath : uri.OriginalString;

        if (path.Contains("/market/v1/quote", StringComparison.OrdinalIgnoreCase))
            return ("market-quotes", QuoteRequestSpacing);

        if (path.Contains("/historical/v1/getCandleData", StringComparison.OrdinalIgnoreCase))
            return ("historical-candles", HistoricalRequestSpacing);

        return null;
    }

    public static bool IsEndpointCoolingDown(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        var endpoint = GetRateLimitedEndpoint(uri);
        return endpoint != null
            && EndpointStates.TryGetValue(endpoint.Value.Key, out var state)
            && state.CooldownUntilUtc > DateTime.UtcNow;
    }

    public async Task<T> SendAsync<T>(HttpRequestMessage request)
    {
        string json = string.Empty;

        try
        {
            json = await SendRawAsync(request);

            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;

                if (root.TryGetProperty("success", out var successProperty)
                    && successProperty.ValueKind == JsonValueKind.False)
                {
                    string? message = null;
                    string? errorCode = null;
                    if (root.TryGetProperty("message", out var messageProperty))
                        message = messageProperty.GetString();
                    if (root.TryGetProperty("errorCode", out var errorCodeProperty))
                        errorCode = errorCodeProperty.GetString();

                    _logger.LogWarning(
                        "Angel One API returned failure. ErrorCode={ErrorCode}, Message={Message}, Response={Response}",
                        errorCode, message, json);
                    throw new AngelApiException(System.Net.HttpStatusCode.OK, json);
                }
            }
            catch (JsonException)
            {
                // Let the normal deserializer report malformed response content.
            }

            var result = JsonSerializer.Deserialize<T>(json, JsonOptions);
            if (result == null)
            {
                _logger.LogError("Unable to deserialize response into {Type}. Response={Response}", typeof(T).Name, json);
                throw new InvalidOperationException($"Unable to deserialize response into {typeof(T).Name}.");
            }

            return result;
        }
        catch (AngelApiException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Deserialization failed for {Type}. Response={Response}", typeof(T).Name, json);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while processing response for {Type}. Response={Response}", typeof(T).Name, json);
            throw;
        }
    }
}
