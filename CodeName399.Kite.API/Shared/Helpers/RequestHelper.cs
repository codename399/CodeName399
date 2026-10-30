using CodeName399.Kite.API.Constants;
using CodeName399.Kite.API.Services;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeName399.Kite.API.Helpers;

public class RequestHelper
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RequestHelper> _logger;
    private readonly SmartApiRateLimiter _rateLimiter;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public RequestHelper(
        IHttpClientFactory factory,
        ILogger<RequestHelper> logger,
        SmartApiRateLimiter rateLimiter)
    {
        _httpClient = factory.CreateClient(Constant.AngelRefresh);
        _logger = logger;
        _rateLimiter = rateLimiter;
    }

    public async Task SendAsync(HttpRequestMessage request)
    {
        try
        {
            using var response = await _httpClient.SendAsync(request);

            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                var isHistoricalRateLimit =
                    response.StatusCode == System.Net.HttpStatusCode.Forbidden &&
                    content.Contains("exceeding access rate", StringComparison.OrdinalIgnoreCase);

                if (isHistoricalRateLimit)
                {
                    // Report the cooldown here; HistoricalRateLimiter owns the
                    // rate-limit diagnostic so we do not emit a duplicate HTTP error.
                    _rateLimiter.ReportGlobalRateLimit(TimeSpan.FromSeconds(15));
                }
                else
                {
                    _logger.LogError(
                        "HTTP {Method} {Url} failed. Status={StatusCode}. Response={Response}",
                        request.Method,
                        request.RequestUri,
                        (int)response.StatusCode,
                        content
                    );
                }

                throw new AngelApiException(response.StatusCode, content);
            }
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
    }

    public async Task<string> SendRawAsync(HttpRequestMessage request)
    {
        try
        {
            using var response = await _httpClient.SendAsync(request);

            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                var isHistoricalRateLimit =
                    response.StatusCode == System.Net.HttpStatusCode.Forbidden &&
                    content.Contains("exceeding access rate", StringComparison.OrdinalIgnoreCase);

                if (isHistoricalRateLimit)
                {
                    // Report the cooldown here; HistoricalRateLimiter owns the
                    // rate-limit diagnostic so we do not emit a duplicate HTTP error.
                    _rateLimiter.ReportGlobalRateLimit(TimeSpan.FromSeconds(15));
                }
                else
                {
                    _logger.LogError(
                        "HTTP {Method} {Url} failed. Status={StatusCode}. Response={Response}",
                        request.Method,
                        request.RequestUri,
                        (int)response.StatusCode,
                        content
                    );
                }

                throw new AngelApiException(response.StatusCode, content);
            }

            return content;
        }
        catch (AngelApiException ex)
        {
            _logger.LogError(ex, "HTTP {Method} {Url} failed.", request.Method, request.RequestUri);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HTTP {Method} {Url} failed.", request.Method, request.RequestUri);

            throw;
        }
    }

    public async Task<T> SendAsync<T>(HttpRequestMessage request)
    {
        string json = string.Empty;

        try
        {
            json = await SendRawAsync(request);

            // Detect Angel One API failures before deserialization.
            try
            {
                using var document = JsonDocument.Parse(json);

                var root = document.RootElement;

                if (
                    root.TryGetProperty("success", out var successProperty)
                    && successProperty.ValueKind == JsonValueKind.False
                )
                {
                    string? message = null;
                    string? errorCode = null;

                    if (root.TryGetProperty("message", out var messageProperty))
                        message = messageProperty.GetString();

                    if (root.TryGetProperty("errorCode", out var errorCodeProperty))
                        errorCode = errorCodeProperty.GetString();

                    _logger.LogWarning(
                        "Angel One API returned failure. ErrorCode={ErrorCode}, Message={Message}, Response={Response}",
                        errorCode,
                        message,
                        json
                    );

                    throw new AngelApiException(System.Net.HttpStatusCode.OK, json);
                }
            }
            catch (JsonException)
            {
                // Ignore JSON inspection failures and let the normal deserializer handle them.
            }

            var result = JsonSerializer.Deserialize<T>(json, JsonOptions);

            if (result == null)
            {
                _logger.LogError(
                    "Unable to deserialize response into {Type}. Response={Response}",
                    typeof(T).Name,
                    json
                );

                throw new InvalidOperationException(
                    $"Unable to deserialize response into {typeof(T).Name}."
                );
            }

            return result;
        }
        catch (AngelApiException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            _logger.LogError(
                ex,
                "Deserialization failed for {Type}. Response={Response}",
                typeof(T).Name,
                json
            );

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error while processing response for {Type}. Response={Response}",
                typeof(T).Name,
                json
            );

            throw;
        }
    }
}
