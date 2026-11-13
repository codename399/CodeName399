using CodeName399.CandleBuilder.API.Constants;
using CodeName399.CandleBuilder.API.Extensions;
using CodeName399.CandleBuilder.API.Helpers;
using CodeName399.CandleBuilder.API.Models;
using DatabaseRepository.Common.Utilities;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeName399.CandleBuilder.API.Services;

public partial class AngelOneService : IAngelOneService
{
    private readonly HttpClient _httpClient;
    private readonly AngelOneConfiguration _options;
    private readonly ILogger<AngelOneService> _logger;
    private readonly RequestHelper _requestHelper;
    private readonly SemaphoreSlim _loginLock = new(1, 1);
    private AngelOneLoginData? _login;
    private DateTime _lastLoginUtc = DateTime.MinValue;
    private volatile bool _lastPositionRefreshSucceeded;
    private static readonly TimeSpan LoginRefreshInterval = TimeSpan.FromMinutes(55);

    public AngelOneService(
        IHttpClientFactory factory,
        IOptions<AngelOneConfiguration> options,
        ILogger<AngelOneService> logger,
        RequestHelper requestHelper
    )
    {
        _httpClient = factory.CreateClient("AngelRefresh");
        _options = options.Value;
        _logger = logger;
        _requestHelper = requestHelper;
    }

    private bool SessionExpired
    {
        get
        {
            if (_login == null || string.IsNullOrWhiteSpace(_login.JwtToken)) return true;
            if (DateTime.UtcNow - _lastLoginUtc >= LoginRefreshInterval) return true;
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
                return TimeZoneInfo.ConvertTimeFromUtc(_lastLoginUtc, tz).Date != TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date;
            }
            catch { return false; }
        }
    }

    public async Task<AngelOneLoginData> GetLoginDataAsync()
    {
        if (!SessionExpired)
            return _login!;

        await _loginLock.WaitAsync();

        try
        {
            if (!SessionExpired)
                return _login!;

            if (_login == null)
            {
                _logger.LogInformation("Performing Angel One login.");

                await LoginInternalAsync();
            }
            else
            {
                try
                {
                    _logger.LogInformation("Refreshing Angel One JWT.");

                    await RefreshInternalAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "JWT refresh failed. Performing full login.");

                    await LoginInternalAsync();
                }
            }

            return _login!;
        }
        finally
        {
            _loginLock.Release();
        }
    }

    private async Task LoginInternalAsync()
    {
        var requestBody = new
        {
            clientcode = _options.ClientCode,
            password = _options.Pin,
            totp = Util.GenerateTOtp(_options.TotpSecret),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, AngelApiUrlConstant.loginUrl);

        request.AddAngelOneHeaders(_options);

        request.AddContent(requestBody);

        AngelOneLoginResponse login;

        try
        {
            login = await _requestHelper.SendAsync<AngelOneLoginResponse>(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Angel One login failed.");
            throw;
        }

        if (login.Data == null)
        {
            _logger.LogError("Angel One login returned no data.");
            throw new Exception("Angel One login returned no data.");
        }

        _login = login.Data;

        _lastLoginUtc = DateTime.UtcNow;

        _logger.LogInformation("Angel One login successful.");
    }

    private async Task RefreshInternalAsync()
    {
        if (_login == null || string.IsNullOrWhiteSpace(_login.RefreshToken))
        {
            throw new Exception("Refresh token missing.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            AngelApiUrlConstant.generateTokenUrl
        );

        request.AddAngelOneHeaders(_options);

        request.AddContent(new { refreshToken = _login.RefreshToken });

        AngelOneLoginResponse result;

        try
        {
            result = await _requestHelper.SendAsync<AngelOneLoginResponse>(request);
        }
        catch (AngelApiException)
        {
            // RequestHelper has already logged the actual API response.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Angel One token refresh failed.");
            throw;
        }

        if (!result.Success)
        {
            _logger.LogWarning(
                "Angel One rejected JWT refresh. ErrorCode={ErrorCode}, Message={Message}",
                result.ErrorCode,
                result.Message
            );

            throw new Exception(result.Message ?? "JWT refresh failed.");
        }

        if (result.Data == null)
        {
            _logger.LogError("Refresh returned empty login data.");

            throw new Exception("Refresh returned empty login data.");
        }

        _login = result.Data;
        _lastLoginUtc = DateTime.UtcNow;

        _logger.LogInformation("Angel One JWT refreshed.");
    }
}
