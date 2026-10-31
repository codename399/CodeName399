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
    private readonly PaperPortfolioService _paperPortfolioService;
    private readonly ILogger<AngelOneService> _logger;
    private readonly RequestHelper _requestHelper;
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
        PaperPortfolioService paperPortfolioService
    )
    {
        _httpClient = factory.CreateClient(Constant.AngelRefresh);
        _options = options.Value;
        _tradingSettingsProvider = tradingSettingsProvider;
        _logger = logger;
        _requestHelper = requestHelper;
        _paperPortfolioService = paperPortfolioService;
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

    public async Task<decimal> GetAvailableBalanceAsync()
    {
        var settings = _tradingSettingsProvider.Current;

        if (!settings.EnableAutoTrading)
            return await _paperPortfolioService.GetAvailableBalanceAsync();

        using var request = await CreateRequestAsync(HttpMethod.Get, AngelApiUrlConstant.getRmsUrl);

        var result = await _requestHelper.SendAsync<RmsResponse>(request);

        return result.Data?.AvailableCash ?? 0;
    }

    public async Task<PlaceOrderResponse> PlaceOrderAsync(PlaceOrderRequest requestBody, bool forceLive = false)
    {
        _logger.LogInformation("BROKER ORDER PREFLIGHT Symbol={Symbol} Token={Token} Exchange={Exchange} Side={Side} Quantity={Quantity} OrderType={OrderType} ProductType={ProductType} Duration={Duration} ForceLive={ForceLive} PaperTrading={PaperTrading} AutoTrading={AutoTrading} IsExitOrder={IsExitOrder}", requestBody.TradingSymbol, requestBody.SymbolToken, requestBody.Exchange, requestBody.TransactionType, requestBody.Quantity, requestBody.OrderType, requestBody.ProductType, requestBody.Duration, forceLive, !_tradingSettingsProvider.Current.EnableAutoTrading, _tradingSettingsProvider.Current.EnableAutoTrading, requestBody.IsExitOrder);
        if (!_tradingSettingsProvider.Current.EnableAutoTrading)
        {
            _logger.LogWarning("BROKER ORDER BLOCKED Symbol={Symbol} Gate=PaperTrading Reason=Live broker order blocked by PaperTrading setting.", requestBody.TradingSymbol);
            return new PlaceOrderResponse { Status = false, Message = "Paper trading is enabled; live broker order blocked." };
        }

        if (!string.IsNullOrWhiteSpace(requestBody.OrderTag) && requestBody.OrderTag.Length >= 20)
        {
            _logger.LogWarning("BROKER ORDER BLOCKED Symbol={Symbol} Gate=OrderTag TagLength={TagLength}", requestBody.TradingSymbol, requestBody.OrderTag.Length);
            return new PlaceOrderResponse { Status = false, Message = "SmartAPI order tag must be fewer than 20 characters." };
        }

        var liveTrading = true;

        if (liveTrading && !ValidateRegisteredStaticIpForLiveOrder(out var staticIpError))
        {
            _logger.LogError("BROKER ORDER BLOCKED Symbol={Symbol} Gate=StaticIP Reason={Reason}", requestBody.TradingSymbol, staticIpError);
            return new PlaceOrderResponse { Status = false, Message = staticIpError };
        }

        var marketNow = GetMarketNow();
        var entryCutoff = ResolveEntryCutoff(requestBody);
        if (!requestBody.IsExitOrder && marketNow.TimeOfDay >= entryCutoff)
        {
            var reason = $"New intraday entries are blocked after {entryCutoff}. Existing positions may still be exited.";
            _logger.LogWarning("BROKER ORDER BLOCKED Symbol={Symbol} Gate=EntryCutoff MarketTime={MarketTime} EntryCutoff={EntryCutoff}", requestBody.TradingSymbol, marketNow, entryCutoff);
            return new PlaceOrderResponse { Status = false, Message = reason };
        }

        using var request = await CreateRequestAsync(
            HttpMethod.Post,
            AngelApiUrlConstant.placeOrderUrl,
            requestBody
        );

        try
        {
            _logger.LogInformation("BROKER ORDER HTTP REQUEST Sending order to broker. Symbol={Symbol} Token={Token} Side={Side} Quantity={Quantity}", requestBody.TradingSymbol, requestBody.SymbolToken, requestBody.TransactionType, requestBody.Quantity);
            var response = await _requestHelper.SendAsync<PlaceOrderResponse>(request);
            _logger.LogInformation("BROKER ORDER HTTP RESPONSE Symbol={Symbol} Status={Status} Message={Message} OrderId={OrderId} UniqueOrderId={UniqueOrderId}", requestBody.TradingSymbol, response.Status, response.Message, response.Data?.OrderId, response.Data?.UniqueOrderId);
            return response;
        }
        catch (AngelApiException ex)
        {
            _logger.LogWarning(
                ex,
                "BROKER ORDER HTTP FAILURE Symbol={Symbol} Side={Side} Quantity={Quantity} Status={Status} Response={Response}",
                requestBody.TradingSymbol, requestBody.TransactionType, requestBody.Quantity, ex.StatusCode, ex.Response
            );

            return new PlaceOrderResponse { Status = false, Message = ex.Response };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BROKER ORDER HTTP EXCEPTION Symbol={Symbol} Side={Side} Quantity={Quantity}; unexpected error while placing order.", requestBody.TradingSymbol, requestBody.TransactionType, requestBody.Quantity);

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

        using var request = await CreateRequestAsync(
            HttpMethod.Post,
            AngelApiUrlConstant.cancelOrderUrl,
            new { orderid = orderId, variety }
        );

        try
        {
            _logger.LogInformation("BROKER CANCEL HTTP REQUEST OrderId={OrderId} Variety={Variety}", orderId, variety);
            var response = await _requestHelper.SendAsync<PlaceOrderResponse>(request);
            _logger.LogInformation("BROKER CANCEL HTTP RESPONSE OrderId={OrderId} Status={Status} Message={Message}", orderId, response.Status, response.Message);
            return response;
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

        using var request = await CreateRequestAsync(
            HttpMethod.Post,
            AngelApiUrlConstant.modifyOrderUrl,
            modifyRequest
        );

        try
        {
            _logger.LogInformation("BROKER MODIFY HTTP REQUEST Symbol={Symbol} Token={Token} OrderType={OrderType} Quantity={Quantity} OrderId={OrderId}", modifyRequest.TradingSymbol, modifyRequest.SymbolToken, modifyRequest.OrderType, modifyRequest.Quantity, modifyRequest.OrderId);
            var response = await _requestHelper.SendAsync<PlaceOrderResponse>(request);
            _logger.LogInformation("BROKER MODIFY HTTP RESPONSE Symbol={Symbol} Status={Status} Message={Message} OrderId={OrderId}", modifyRequest.TradingSymbol, response.Status, response.Message, response.Data?.OrderId);
            return response;
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
        // Margin calculation is read-only and should use the broker's actual
        // product-specific requirement even in paper-trading mode. Returning
        // price * quantity here incorrectly treats paper trades as full-cash-funded
        // orders and makes affordable quantity much lower than the platform's
        // INTRADAY margin (for example, 5x leverage).
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
        // All intraday buy orders stop 30 minutes before the configured market close.
        return _tradingSettingsProvider.Current.IntradayCutoffTime;
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

    public async Task<decimal?> GetLtpDataAsync(string exchange, string tradingSymbol, string symbolToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(exchange) || string.IsNullOrWhiteSpace(tradingSymbol) || string.IsNullOrWhiteSpace(symbolToken))
            return null;

        var login = await GetLoginDataAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, AngelApiUrlConstant.ltpDataUrl);
        request.AddAngelOneHeaders(_options, login.JwtToken);
        request.AddContent(new { exchange, tradingsymbol = tradingSymbol, symboltoken = symbolToken });

        try
        {
            var result = await _requestHelper.SendAsync<LtpDataResponse>(request);
            var ltp = result.Data?.Ltp ?? 0m;
            if (ltp <= 0m)
            {
                _logger.LogWarning("Angel One returned invalid LTP for {Exchange}:{Symbol} token={Token}", exchange, tradingSymbol, symbolToken);
                return null;
            }
            return ltp;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to retrieve LTP for {Exchange}:{Symbol} token={Token}", exchange, tradingSymbol, symbolToken);
            return null;
        }
    }

    public async Task<OrderData?> GetIndividualOrderAsync(string uniqueOrderId)
    {
        if (string.IsNullOrWhiteSpace(uniqueOrderId))
            return null;

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
