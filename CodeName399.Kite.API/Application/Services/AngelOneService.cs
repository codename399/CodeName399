using CodeName399.Kite.API.Constants;
using CodeName399.Kite.API.Extensions;
using CodeName399.Kite.API.Helpers;
using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Enum;
using CodeName399.Kite.API.Models.WebSocketOrder;
using DatabaseRepository.Common.Utilities;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeName399.Kite.API.Services;

public partial class AngelOneService : IAngelOneService
{
    private readonly HttpClient _httpClient;
    private readonly AngelOneConfiguration _options;
    private readonly TradingSettingsProvider _tradingSettingsProvider;
    private readonly ILogger<AngelOneService> _logger;
    private readonly RequestHelper _requestHelper;
    private readonly SmartApiRateLimiter _rateLimiter;
    private readonly PaperPortfolioService _paperPortfolio;
    private readonly CasTradingRulesService _casRules;
    private readonly CasEligibilityService _casEligibility;

    private readonly SemaphoreSlim _loginLock = new(1, 1);
    private readonly SemaphoreSlim _positionSemaphore = new(1, 1);

    private AngelOneLoginData? _login;

    private DateTime _lastLoginUtc = DateTime.MinValue;

    private DateTime _positionsLastLoaded = DateTime.MinValue;

    private List<Position> _cachedPositions = [];
    private volatile bool _lastPositionRefreshSucceeded;

    private static readonly TimeSpan LoginRefreshInterval = TimeSpan.FromMinutes(55);

    private static readonly TimeSpan PositionCacheDuration = TimeSpan.FromSeconds(10);

    public bool LastPositionRefreshSucceeded => _lastPositionRefreshSucceeded;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public AngelOneService(
        IHttpClientFactory factory,
        IOptions<AngelOneConfiguration> options,
        TradingSettingsProvider tradingSettingsProvider,
        ILogger<AngelOneService> logger,
        RequestHelper requestHelper,
        SmartApiRateLimiter rateLimiter,
        PaperPortfolioService paperPortfolio,
        CasTradingRulesService casRules,
        CasEligibilityService casEligibility
    )
    {
        _httpClient = factory.CreateClient(Constant.AngelRefresh);
        _options = options.Value;
        _tradingSettingsProvider = tradingSettingsProvider;
        _logger = logger;
        _requestHelper = requestHelper;
        _rateLimiter = rateLimiter;
        _paperPortfolio = paperPortfolio;
        _casRules = casRules;
        _casEligibility = casEligibility;
    }

    private bool SessionExpired
    {
        get
        {
            if (_login == null || string.IsNullOrWhiteSpace(_login.JwtToken)) return true;
            if (DateTime.UtcNow - _lastLoginUtc >= LoginRefreshInterval) return true;
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(_tradingSettingsProvider.Current.MarketTimeZoneId);
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

        await _rateLimiter.WaitAsync("login");
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

        await _rateLimiter.WaitAsync("tokenrefresh");
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

    private async Task<HttpRequestMessage> CreateRequestAsync(
        HttpMethod method,
        string url,
        object? body = null
    )
    {
        var login = await GetLoginDataAsync();

        var request = new HttpRequestMessage(method, url);

        request.AddAngelOneHeaders(_options, login.JwtToken);

        if (body != null)
        {
            request.AddContent(body);
        }

        return request;
    }

    public async Task<decimal> GetAvailableBalanceAsync(bool forceLive = false)
    {
        var settings = _tradingSettingsProvider.Current;

        if (settings.PaperTrading && !forceLive)
            return await _paperPortfolio.GetAvailableBalanceAsync();

        await _rateLimiter.WaitAsync("rms");
        using var request = await CreateRequestAsync(HttpMethod.Get, AngelApiUrlConstant.getRmsUrl);

        var result = await _requestHelper.SendAsync<RmsResponse>(request);

        return result.Data?.AvailableCash ?? 0;
    }

    public async Task<PlaceOrderResponse> PlaceOrderAsync(PlaceOrderRequest requestBody, bool forceLive = false)
    {
        if (_tradingSettingsProvider.Current.PaperTrading && !forceLive)
            return new PlaceOrderResponse { Status = false, Message = "Paper trading is enabled; live broker order blocked." };

        if (!string.IsNullOrWhiteSpace(requestBody.OrderTag) && requestBody.OrderTag.Length >= 20)
            return new PlaceOrderResponse { Status = false, Message = "SmartAPI order tag must be fewer than 20 characters." };

        var liveTrading = true;
        if (liveTrading && !ValidateRegisteredStaticIpForLiveOrder(out var staticIpError))
            return new PlaceOrderResponse { Status = false, Message = staticIpError };

        requestBody.IsCasEligible = requestBody.IsCasEligible ||
            await _casEligibility.IsCasEligibleAsync(requestBody.Exchange, requestBody.SymbolToken);

        var marketNow = GetMarketNow();
        var entryCutoff = ResolveEntryCutoff(requestBody);
        if (!requestBody.IsExitOrder && marketNow.TimeOfDay >= entryCutoff)
            return new PlaceOrderResponse { Status = false, Message = $"New intraday entries are blocked after {entryCutoff}. Existing positions may still be exited." };

        var casResult = _casRules.ValidatePlace(requestBody, marketNow);
        if (!casResult.Allowed)
            return new PlaceOrderResponse { Status = false, Message = casResult.Reason };

        if (_tradingSettingsProvider.Current.EnableScripConsentForCashOrders
            && requestBody.Exchange is "NSE" or "BSE")
        {
            // SmartAPI documents scripconsent as the consent flag for cash-market
            // surveillance scrips. Sending yes for configured cash orders is safe
            // and avoids silently dropping a required consent flag when the
            // broker's surveillance classification changes intraday.
            requestBody.ScripConsent = "yes";
        }

        if (requestBody.IsCasEligible
            && requestBody.OrderType.Equals("LIMIT", StringComparison.OrdinalIgnoreCase)
            && marketNow.TimeOfDay >= _tradingSettingsProvider.Current.CasOrderEntryStart
            && marketNow.TimeOfDay < _tradingSettingsProvider.Current.CasLimitOnlyEnd)
        {
            var reference = await GetReferencePriceAsync(requestBody.Exchange, requestBody.SymbolToken, requestBody.TradingSymbol);
            if (reference is null)
                return new PlaceOrderResponse { Status = false, Message = "CAS reference price unavailable; LIMIT order blocked for safety." };

            if (requestBody.Price < reference.LowerBand || requestBody.Price > reference.UpperBand)
                return new PlaceOrderResponse { Status = false, Message = $"CAS price {requestBody.Price} is outside the reference-price band {reference.LowerBand:F2}-{reference.UpperBand:F2}." };
        }

        await _rateLimiter.WaitAsync("order");
        using var request = await CreateRequestAsync(
            HttpMethod.Post,
            AngelApiUrlConstant.placeOrderUrl,
            requestBody
        );

        try
        {
            return await _requestHelper.SendAsync<PlaceOrderResponse>(request);
        }
        catch (AngelApiException ex)
        {
            _logger.LogWarning(
                "PlaceOrder failed. Status={Status} Response={Response}",
                ex.StatusCode,
                ex.Response
            );

            return new PlaceOrderResponse { Status = false, Message = ex.Response };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while placing order.");

            return new PlaceOrderResponse { Status = false, Message = ex.Message };
        }
    }

    public bool ValidateRegisteredStaticIp(out string error) => ValidateRegisteredStaticIpForLiveOrder(out error);

    private bool ValidateRegisteredStaticIpForLiveOrder(out string error)
    {
        error = string.Empty;
        if (!_options.RequireRegisteredStaticIpForLiveTrading)
            return true;

        var configuredPublicIp = string.IsNullOrWhiteSpace(_options.PublicIp)
            ? Environment.GetEnvironmentVariable("ANGEL_PUBLIC_IP")
            : _options.PublicIp;

        var registeredIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(_options.RegisteredStaticIp))
            registeredIps.Add(_options.RegisteredStaticIp.Trim());

        foreach (var ip in _options.RegisteredStaticIps ?? [])
            if (!string.IsNullOrWhiteSpace(ip))
                registeredIps.Add(ip.Trim());

        var envRegistered = Environment.GetEnvironmentVariable("ANGEL_REGISTERED_STATIC_IP");
        if (!string.IsNullOrWhiteSpace(envRegistered))
            registeredIps.Add(envRegistered.Trim());

        if (string.IsNullOrWhiteSpace(configuredPublicIp)
            || registeredIps.Count == 0
            || !registeredIps.Contains(configuredPublicIp.Trim()))
        {
            error = "Registered SmartAPI static IP validation failed. Live order/modify/cancel blocked.";
            return false;
        }

        return true;
    }

    public async Task<List<Position>> GetPositionsAsync(bool forceRefresh = false)
    {
        if (!forceRefresh && DateTime.UtcNow - _positionsLastLoaded < PositionCacheDuration)
        {
            return _cachedPositions;
        }

        await _positionSemaphore.WaitAsync();

        try
        {
            if (!forceRefresh && DateTime.UtcNow - _positionsLastLoaded < PositionCacheDuration)
            {
                return _cachedPositions;
            }

            await _rateLimiter.WaitAsync("positions");
            _lastPositionRefreshSucceeded = false;
            using var request = await CreateRequestAsync(
                HttpMethod.Get,
                AngelApiUrlConstant.positionUrl
            );

            PositionResponse result;

            try
            {
                result = await _requestHelper.SendAsync<PositionResponse>(request);
            }
            catch (AngelApiException ex)
            {
                _logger.LogWarning(
                    "Unable to fetch positions. Status={Status} Response={Response}",
                    ex.StatusCode,
                    ex.Response
                );

                return _cachedPositions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to fetch positions.");

                return _cachedPositions;
            }

            var excluded =
                _tradingSettingsProvider
                    .Current.ExcludedSymbols?.Select(x => x.Trim().ToUpperInvariant())
                    .ToHashSet()
                ?? [];

            _cachedPositions =
                result
                    ?.Data?.Where(x =>
                        !string.IsNullOrWhiteSpace(x.TradingSymbol)
                        && !excluded.Contains(x.TradingSymbol.ToUpperInvariant())
                    )
                    .ToList()
                ?? [];

            _positionsLastLoaded = DateTime.UtcNow;
            _lastPositionRefreshSucceeded = true;

            return _cachedPositions;
        }
        finally
        {
            _positionSemaphore.Release();
        }
    }

    public async Task RefreshPositionsAsync()
    {
        _positionsLastLoaded = DateTime.MinValue;

        await GetPositionsAsync(true);
    }

    public async Task<List<OrderBook>> GetOrderBookAsync()
    {
        await _rateLimiter.WaitAsync("orderbook");
        using var request = await CreateRequestAsync(
            HttpMethod.Get,
            AngelApiUrlConstant.orderBookUrl
        );

        try
        {
            var result = await _requestHelper.SendAsync<OrderBookResponse>(request);

            return result.Data ?? [];
        }
        catch (AngelApiException ex)
        {
            _logger.LogWarning(
                "Unable to fetch order book. Status={Status} Response={Response}",
                ex.StatusCode,
                ex.Response
            );

            return [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while fetching order book.");

            return [];
        }
    }

    public async Task<bool> SquareOffAsync(string symbol, int quantity, TradeDirection direction = TradeDirection.Long)
    {
        var position = (await GetPositionsAsync(true)).FirstOrDefault(x =>
            string.Equals(x.TradingSymbol, symbol, StringComparison.OrdinalIgnoreCase)
            && (string.Equals(x.Exchange, "NSE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(x.Exchange, "BSE", StringComparison.OrdinalIgnoreCase)));

        if (position == null)
            return false;

        var response = await PlaceOrderAsync(
            new PlaceOrderRequest
            {
                TradingSymbol = position.TradingSymbol,
                SymbolToken = position.SymbolToken,
                Exchange = position.Exchange,
                TransactionType = direction == TradeDirection.Short ? "BUY" : "SELL",
                ProductType = position.ProductType,
                OrderType = "MARKET",
                Variety = "NORMAL",
                Duration = "DAY",
                Quantity = quantity,
                IsExitOrder = true,
            },
            forceLive: true
        );

        if (!response.Status)
        {
            _logger.LogWarning(
                "SquareOff failed for {Symbol}. {Message}",
                symbol,
                response.Message
            );
        }

        return response.Status;
    }

    public async Task<bool> SquareOffAsync(string exchange, string symbolToken, int quantity, TradeDirection direction = TradeDirection.Long)
    {
        var position = (await GetPositionsAsync(true)).FirstOrDefault(x =>
            string.Equals(x.Exchange, exchange, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.SymbolToken, symbolToken, StringComparison.OrdinalIgnoreCase) && x.NetQty != 0);
        if (position == null) return false;
        var response = await PlaceOrderAsync(new PlaceOrderRequest
        {
            TradingSymbol = position.TradingSymbol,
            SymbolToken = position.SymbolToken,
            Exchange = position.Exchange,
            TransactionType = direction == TradeDirection.Short ? "BUY" : "SELL",
            ProductType = position.ProductType,
            OrderType = "MARKET",
            Variety = "NORMAL",
            Duration = "DAY",
            Quantity = Math.Min(quantity, Math.Abs(position.NetQty)),
            IsExitOrder = true,
            OrderTag = $"CN399-SQ-{Guid.NewGuid():N}"[..19]
        }, forceLive: true);
        return response.Status;
    }

    public async Task<bool> CancelOrderAsync(string orderId)
    {
        var result = await CancelOrderAsync(orderId, "NORMAL");

        return result.Status;
    }

    public async Task<PlaceOrderResponse> CancelOrderAsync(string orderId, string variety)
    {
        if (!ValidateRegisteredStaticIpForLiveOrder(out var staticIpError))
            return new PlaceOrderResponse { Status = false, Message = staticIpError };

        var details = await GetIndividualOrderAsync(orderId);
        if (details != null)
        {
            var casEligible = await _casEligibility.IsCasEligibleAsync(details.Exchange, details.SymbolToken);
            var casResult = _casRules.ValidateCancel(details.Exchange, details.OrderType, casEligible, GetMarketNow());
            if (!casResult.Allowed)
                return new PlaceOrderResponse { Status = false, Message = casResult.Reason };
        }
        else if (GetMarketNow().TimeOfDay >= _tradingSettingsProvider.Current.CasMarketOnlyEnd
                 && GetMarketNow().TimeOfDay < _tradingSettingsProvider.Current.CasLimitOnlyEnd)
        {
            return new PlaceOrderResponse { Status = false, Message = "Unable to verify broker order details during CAS; cancellation blocked for safety." };
        }

        await _rateLimiter.WaitAsync("cancelorder");
        using var request = await CreateRequestAsync(
            HttpMethod.Post,
            AngelApiUrlConstant.cancelOrderUrl,
            new { orderid = orderId, variety }
        );

        try
        {
            return await _requestHelper.SendAsync<PlaceOrderResponse>(request);
        }
        catch (AngelApiException ex)
        {
            _logger.LogWarning(
                "CancelOrder failed. OrderId={OrderId} Status={Status} Response={Response}",
                orderId,
                ex.StatusCode,
                ex.Response
            );

            return new PlaceOrderResponse { Status = false, Message = ex.Response };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while cancelling order {OrderId}.", orderId);

            return new PlaceOrderResponse { Status = false, Message = ex.Message };
        }
    }

    public async Task<PlaceOrderResponse> ModifyOrderAsync(ModifyOrderRequest modifyRequest)
    {
        if (!ValidateRegisteredStaticIpForLiveOrder(out var staticIpError))
            return new PlaceOrderResponse { Status = false, Message = staticIpError };

        modifyRequest.IsCasEligible = modifyRequest.IsCasEligible ||
            await _casEligibility.IsCasEligibleAsync(modifyRequest.Exchange, modifyRequest.SymbolToken);
        var marketNow = GetMarketNow();
        var casResult = _casRules.ValidateModify(modifyRequest, marketNow);
        if (!casResult.Allowed)
            return new PlaceOrderResponse { Status = false, Message = casResult.Reason };

        if (modifyRequest.IsCasEligible
            && modifyRequest.OrderType.Equals("LIMIT", StringComparison.OrdinalIgnoreCase)
            && marketNow.TimeOfDay >= _tradingSettingsProvider.Current.CasOrderEntryStart
            && marketNow.TimeOfDay < _tradingSettingsProvider.Current.CasLimitOnlyEnd)
        {
            var reference = await GetReferencePriceAsync(modifyRequest.Exchange, modifyRequest.SymbolToken, modifyRequest.TradingSymbol);
            if (reference is null)
                return new PlaceOrderResponse { Status = false, Message = "CAS reference price unavailable; LIMIT modification blocked for safety." };
            if (modifyRequest.Price < reference.LowerBand || modifyRequest.Price > reference.UpperBand)
                return new PlaceOrderResponse { Status = false, Message = $"CAS price {modifyRequest.Price} is outside the reference-price band {reference.LowerBand:F2}-{reference.UpperBand:F2}." };
        }

        await _rateLimiter.WaitAsync("modifyorder");
        using var request = await CreateRequestAsync(
            HttpMethod.Post,
            AngelApiUrlConstant.modifyOrderUrl,
            modifyRequest
        );

        try
        {
            return await _requestHelper.SendAsync<PlaceOrderResponse>(request);
        }
        catch (AngelApiException ex)
        {
            _logger.LogWarning(
                "ModifyOrder failed. Status={Status} Response={Response}",
                ex.StatusCode,
                ex.Response
            );

            return new PlaceOrderResponse { Status = false, Message = ex.Response };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while modifying order.");

            return new PlaceOrderResponse { Status = false, Message = ex.Message };
        }
    }

    public async Task<BrokerageEstimateResponse> EstimateChargesAsync(
        BrokerageEstimateRequest estimateRequest
    )
    {
        using var request = await CreateRequestAsync(
            HttpMethod.Post,
            AngelApiUrlConstant.estimateCharges,
            estimateRequest
        );

        try
        {
            var result = await _requestHelper.SendAsync<BrokerageEstimateResponse>(request);

            if (!result.Status)
            {
                _logger.LogWarning("Brokerage estimate failed. Message={Message}", result.Message);

                throw new Exception(result.Message);
            }

            return result;
        }
        catch (AngelApiException ex)
        {
            _logger.LogError(
                ex,
                "Brokerage estimate request failed. Status={Status} Response={Response}",
                ex.StatusCode,
                ex.Response
            );

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while estimating brokerage.");

            throw;
        }
    }

    public async Task<decimal> GetRequiredMarginAsync(
        string symbolToken,
        int quantity,
        decimal price,
        string exchange = "NSE",
        string productType = "INTRADAY",
        string tradeType = "BUY",
        bool forceLive = false
    )
    {
        if (_tradingSettingsProvider.Current.PaperTrading && !forceLive)
            return price * quantity;

        return await GetRequiredMarginAsync(
            new MarginRequest
            {
                Positions =
                [
                    new MarginPosition
                    {
                        Exchange = exchange,
                        Token = symbolToken,
                        Qty = quantity,
                        Price = price,
                        ProductType = productType,
                        TradeType = tradeType,
                        OrderType = "MARKET",
                    },
                ],
            }
        );
    }

    public async Task<decimal> GetRequiredMarginAsync(MarginRequest marginRequest)
    {
        await _rateLimiter.WaitAsync("margin");
        using var request = await CreateRequestAsync(
            HttpMethod.Post,
            AngelApiUrlConstant.marginCalculatorUrl,
            marginRequest
        );

        try
        {
            var result = await _requestHelper.SendAsync<MarginResponse>(request);

            if (result.Data == null)
            {
                _logger.LogError("Margin API returned invalid response.");

                throw new Exception("Margin API returned invalid response.");
            }

            if (!result.Status)
            {
                _logger.LogWarning("Margin calculation failed. Message={Message}", result.Message);

                throw new Exception(result.Message);
            }

            return result.Data.TotalMarginRequired;
        }
        catch (AngelApiException ex)
        {
            _logger.LogError(
                ex,
                "Margin calculation request failed. Status={Status} Response={Response}",
                ex.StatusCode,
                ex.Response
            );

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while calculating margin.");

            throw;
        }
    }

    private TimeSpan ResolveEntryCutoff(PlaceOrderRequest request)
    {
        var settings = _tradingSettingsProvider.Current;
        var product = request.ProductType ?? string.Empty;
        var squareOff =
            product.Equals("BO", StringComparison.OrdinalIgnoreCase) || product.Equals("ROBO", StringComparison.OrdinalIgnoreCase)
                ? settings.RoboAutoSquareOffTime
                : settings.EquityMisAutoSquareOffTime;
        return new[] { settings.IntradayEntryCutoffTime, squareOff }.Min();
    }


    private DateTime GetMarketNow()
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(_tradingSettingsProvider.Current.MarketTimeZoneId);
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        }
        catch
        {
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, MarketClock.IndiaTimeZone);
        }
    }

    public async Task<ReferencePriceSnapshot?> GetReferencePriceAsync(string exchange, string symbolToken, string tradingSymbol, CancellationToken cancellationToken = default)
    {
        await _rateLimiter.WaitAsync("getltpdata", cancellationToken);
        var login = await GetLoginDataAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, AngelApiUrlConstant.ltpDataUrl);
        request.AddAngelOneHeaders(_options, login.JwtToken);
        request.AddContent(new { exchange, tradingsymbol = tradingSymbol, symboltoken = symbolToken });
        try
        {
            var result = await _requestHelper.SendAsync<LtpDataResponse>(request);
            var data = result.Data;
            if (data == null || data.ReferenceLimitPrice <= 0) return null;
            var band = _tradingSettingsProvider.Current.CasPriceBandPercent / 100m;
            return new ReferencePriceSnapshot(DateOnly.FromDateTime(GetMarketNow()), data.ReferenceLimitPrice, data.ReferenceLimitPrice * (1m - band), data.ReferenceLimitPrice * (1m + band));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to retrieve CAS reference price for {Exchange}:{Token}", exchange, symbolToken);
            return null;
        }
    }

    public async Task<OrderData?> GetIndividualOrderAsync(string uniqueOrderId)
    {
        if (string.IsNullOrWhiteSpace(uniqueOrderId))
            return null;

        await _rateLimiter.WaitAsync("individualorder");
        using var request = await CreateRequestAsync(
            HttpMethod.Get,
            $"{AngelApiUrlConstant.individualOrderUrl}/{uniqueOrderId}"
        );

        try
        {
            var result = await _requestHelper.SendAsync<OrderStatusResponse>(request);

            return result.OrderData;
        }
        catch (AngelApiException ex)
        {
            _logger.LogWarning(
                "Unable to fetch order {OrderId}. Status={Status} Response={Response}",
                uniqueOrderId,
                ex.StatusCode,
                ex.Response
            );

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while fetching order {OrderId}.", uniqueOrderId);

            return null;
        }
    }
}
