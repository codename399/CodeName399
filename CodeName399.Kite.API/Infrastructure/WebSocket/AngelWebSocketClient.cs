using CodeName399.Kite.API.Constants;
using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.WebSocket;
using CodeName399.Kite.API.WebSocket.Interface;
using Microsoft.Extensions.Options;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace CodeName399.Kite.API.Services;

public class AngelWebSocketClient : IAngelWebSocketClient
{
    private readonly ILogger<AngelWebSocketClient> _logger;
    private readonly IAngelWebSocketConnection _connection;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AngelOneConfiguration _options;
    private readonly TradingSettingsProvider _settings;
    private readonly AngelBinaryParser _parser;
    private readonly LiveMarketCache _cache;
    private readonly Dictionary<int, HashSet<string>> _subscriptions = new();
    private readonly Dictionary<int, HashSet<string>> _baseSubscriptions = new();

    // Protects the desired subscription state. This state is retained
    // while disconnected so recovery/discovery requests are not lost.
    private readonly object _subscriptionLock = new();

    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(Math.Max(1, _settings.Current.WebSocketHeartbeatSeconds));
    private TimeSpan PongTimeout => TimeSpan.FromSeconds(Math.Max(2, _settings.Current.WebSocketPongTimeoutSeconds));
    private readonly WatchListService _watchList;
    private readonly IAngelOneService _angelOneService;
    private readonly RawMarketDataArchiveService _rawMarketData;

    private TimeSpan RetryInitialDelay => TimeSpan.FromSeconds(Math.Max(1, _settings.Current.WebSocketRetryInitialSeconds));
    private TimeSpan MaxRetryDelay => TimeSpan.FromSeconds(Math.Max(RetryInitialDelay.TotalSeconds, _settings.Current.WebSocketRetryMaxSeconds));

    private int _currentRetryAttempt;

    private Timer? _heartbeatTimer;

    private DateTime _lastPong = DateTime.UtcNow;

    private readonly object _reconnectLock = new();

    private int _reconnecting;

    private CancellationTokenSource? _cts;

    private Task? _receiveTask;

    private readonly SemaphoreSlim _sync = new(1, 1);

    // SmartAPI WebSocket endpoint is centralized in AngelApiUrlConstant.socketUrl.
    private const string SocketUrl = AngelApiUrlConstant.socketUrl;

    private int _starting;

    public bool IsConnected => _connection.State == System.Net.WebSockets.WebSocketState.Open;

    public event EventHandler<MarketTick>? TickReceived;

    public AngelWebSocketClient(
        ILogger<AngelWebSocketClient> logger,
        IAngelWebSocketConnection connection,
        IServiceScopeFactory scopeFactory,
        IOptions<AngelOneConfiguration> options,
        TradingSettingsProvider settings,
        AngelBinaryParser parser,
        LiveMarketCache cache,
        WatchListService watchListService,
        IAngelOneService angelOneService,
        RawMarketDataArchiveService rawMarketData
    )
    {
        _logger = logger;
        _connection = connection;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _settings = settings;
        _parser = parser;
        _cache = cache;
        _watchList = watchListService;
        _angelOneService = angelOneService;
        _rawMarketData = rawMarketData;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _starting, 1) == 1)
            return;

        try
        {
            if (IsConnected)
                return;

            _cts?.Cancel();
            _cts?.Dispose();

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var login = await _angelOneService.GetLoginDataAsync();

            var headers = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {login.JwtToken}",
                ["x-api-key"] = _options.ApiKey,
                ["x-client-code"] = _options.ClientCode,
                ["x-feed-token"] = login.FeedToken,
            };

            await _connection.ConnectAsync(new Uri(SocketUrl), headers, _cts.Token);
            _lastPong = DateTime.UtcNow;

            StartHeartbeat();

            _receiveTask = Task.Run(() => ReceiveLoop(_cts.Token));

            // Restore any subscriptions requested before the socket
            // connected or before a reconnect completed.
            await ResubscribeAsync();
        }
        finally
        {
            Interlocked.Exchange(ref _starting, 0);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _cts?.Cancel();

            if (_receiveTask != null)
            {
                try
                {
                    await _receiveTask;
                }
                catch { }

                _receiveTask = null;
            }

            _heartbeatTimer?.Dispose();
            _heartbeatTimer = null;

            await _connection.CloseAsync(cancellationToken);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    public Task SubscribeAsync(IEnumerable<string> tokens)
        => SubscribeAsync(tokens, "NSE");

    public Task SubscribeAsync(IEnumerable<string> tokens, string exchange)
        => SubscribeExchangeAsync(tokens, GetExchangeType(exchange));

    public Task UnsubscribeAsync(IEnumerable<string> tokens, string exchange)
        => UnsubscribeExchangeAsync(tokens, GetExchangeType(exchange));

    private async Task SubscribeExchangeAsync(IEnumerable<string> tokens, int exchangeType)
    {
        var tokenList = tokens.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (tokenList.Count == 0) return;
        lock (_subscriptionLock)
        {
            if (!_baseSubscriptions.TryGetValue(exchangeType, out var baseSet))
                _baseSubscriptions[exchangeType] = baseSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var token in tokenList) baseSet.Add(token);
        }
        await RebuildSubscriptionsAsync();
    }

    private async Task UnsubscribeExchangeAsync(IEnumerable<string> tokens, int exchangeType)
    {
        var tokenList = tokens.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (tokenList.Count == 0) return;
        lock (_subscriptionLock)
        {
            if (_baseSubscriptions.TryGetValue(exchangeType, out var baseSet))
                foreach (var token in tokenList) baseSet.Remove(token);
        }
        await RebuildSubscriptionsAsync();
    }
    public Task UnsubscribeAsync(IEnumerable<string> tokens)
        => UnsubscribeAsync(tokens, "NSE");

    public Task SetSubscriptionsAsync(IEnumerable<string> tokens)
        => SetSubscriptionsAsync(tokens.Select(x => new MarketSubscription(x, "NSE")));

    public async Task SetSubscriptionsAsync(IEnumerable<MarketSubscription> subscriptions)
    {
        var desiredBase = subscriptions.Where(x => !string.IsNullOrWhiteSpace(x.SymbolToken) && !string.IsNullOrWhiteSpace(x.Exchange))
            .GroupBy(x => GetExchangeType(x.Exchange.ToUpperInvariant()))
            .ToDictionary(g => g.Key, g => g.Select(x => x.SymbolToken).ToHashSet(StringComparer.OrdinalIgnoreCase));
        lock (_subscriptionLock)
        {
            _baseSubscriptions.Clear();
            foreach (var pair in desiredBase)
                _baseSubscriptions[pair.Key] = pair.Value;
        }
        await RebuildSubscriptionsAsync();
    }


    private async Task RebuildSubscriptionsAsync()
    {
        var desired = new Dictionary<int, HashSet<string>>();
        lock (_subscriptionLock)
        {
            foreach (var pair in _baseSubscriptions)
                desired[pair.Key] = new HashSet<string>(pair.Value, StringComparer.OrdinalIgnoreCase);
        }

        var add = new List<(int ExchangeType, List<string> Tokens)>();
        var remove = new List<(int ExchangeType, List<string> Tokens)>();
        lock (_subscriptionLock)
        {
            foreach (var key in _subscriptions.Keys.Union(desired.Keys).ToList())
            {
                _subscriptions.TryGetValue(key, out var current);
                current ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                desired.TryGetValue(key, out var wanted);
                wanted ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var a = wanted.Except(current, StringComparer.OrdinalIgnoreCase).ToList();
                var d = current.Except(wanted, StringComparer.OrdinalIgnoreCase).ToList();
                if (a.Count > 0) add.Add((key, a));
                if (d.Count > 0) remove.Add((key, d));
                _subscriptions[key] = wanted;
            }
        }
        if (!IsConnected) return;
        foreach (var item in remove) await SendSubscriptionRequestAsync(AngelWebSocketConstants.Unsubscribe, item.Tokens, item.ExchangeType);
        foreach (var item in add) await SendSubscriptionRequestAsync(AngelWebSocketConstants.Subscribe, item.Tokens, item.ExchangeType);
    }

    private async Task SendSubscriptionRequestAsync(
        int action,
        IEnumerable<string> tokens,
        int exchangeType = 1)
    {
        var tokenList = tokens
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (tokenList.Count == 0 || !IsConnected)
            return;

        var request = new AngelSubscriptionRequest
        {
            Action = action,
            Params = new AngelSubscriptionParams
            {
                Mode = AngelWebSocketConstants.SnapQuote,
                TokenList =
                [
                    new AngelTokenSubscription
                    {
                        ExchangeType = exchangeType,
                        Tokens = tokenList,
                    },
                ],
            },
        };

        await SendAsync(request);
    }

    private async Task ReceiveLoop(CancellationToken token)
    {
        var buffer = new byte[8192];

        while (!token.IsCancellationRequested)
        {
            try
            {
                var result = await _connection.ReceiveAsync(buffer, token);

                switch (result.MessageType)
                {
                    case WebSocketMessageType.Text:
                        await HandleTextMessage(buffer.AsMemory(0, result.Count));
                        break;

                    case WebSocketMessageType.Binary:
                        await HandleBinaryMessage(buffer.AsMemory(0, result.Count));
                        break;

                    case WebSocketMessageType.Close:
                        await ReconnectAsync();
                        return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (WebSocketException ex)
            {
                _logger.LogWarning(ex, "WebSocket receive failed.");

                await ReconnectAsync();
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ReceiveLoop crashed.");

                await ReconnectAsync();
                return;
            }
        }
    }

    private async Task SendAsync<T>(T request)
    {
        var json = JsonSerializer.Serialize(request, _jsonOptions);

        var bytes = Encoding.UTF8.GetBytes(json);

        await _connection.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true);
    }

    private async Task ResubscribeAsync()
    {
        List<AngelTokenSubscription> tokenGroups;

        lock (_subscriptionLock)
        {
            tokenGroups = _subscriptions
                .Where(x => x.Value.Count > 0)
                .Select(x =>
                    new AngelTokenSubscription
                    {
                        ExchangeType = x.Key,
                        Tokens = x.Value.ToList(),
                    })
                .ToList();
        }

        if (tokenGroups.Count == 0 || !IsConnected)
            return;

        var request = new AngelSubscriptionRequest
        {
            Action = AngelWebSocketConstants.Subscribe,
            Params =
            {
                Mode = AngelWebSocketConstants.SnapQuote,
                TokenList = tokenGroups,
            },
        };

        await SendAsync(request);

        var count = tokenGroups.Sum(x => x.Tokens.Count);
    }

    private void StartHeartbeat()
    {
        _heartbeatTimer?.Dispose();

        _heartbeatTimer = new Timer(
            async _ => await SendPingAsync(),
            null,
            HeartbeatInterval,
            HeartbeatInterval
        );
    }

    private async Task SendPingAsync()
    {
        if (!IsConnected)
            return;

        try
        {
            if (DateTime.UtcNow - _lastPong > PongTimeout)
            {
                _logger.LogWarning(
                    "WebSocket heartbeat timed out. LastPong={LastPong}. Reconnecting.",
                    _lastPong);

                await ReconnectAsync();
                return;
            }

            var bytes = Encoding.UTF8.GetBytes("ping");

            await _connection.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ping failed.");
            await ReconnectAsync();
        }
    }

    private async Task ReconnectAsync()
    {
        if (Interlocked.Exchange(ref _reconnecting, 1) == 1)
            return;

        try
        {
            _currentRetryAttempt = 0;
            while (!(_cts?.IsCancellationRequested ?? true))
            {
                _currentRetryAttempt++;
                try
                {
                    _logger.LogWarning("WebSocket reconnect attempt {Attempt}.", _currentRetryAttempt);
                    _heartbeatTimer?.Dispose();
                    _heartbeatTimer = null;
                    try { await _connection.CloseAsync(); } catch { }
                    var seconds = Math.Min(MaxRetryDelay.TotalSeconds, RetryInitialDelay.TotalSeconds * Math.Pow(2, Math.Min(_currentRetryAttempt - 1, 6)));
                    await Task.Delay(TimeSpan.FromSeconds(seconds), _cts?.Token ?? CancellationToken.None);
                    await StartAsync(_cts?.Token ?? CancellationToken.None);
                    if (IsConnected) { _currentRetryAttempt = 0; return; }
                }
                catch (OperationCanceledException) when (_cts?.IsCancellationRequested == true)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "WebSocket reconnect attempt {Attempt} failed.", _currentRetryAttempt);
                }
            }
        }
        finally
        {
            _currentRetryAttempt = 0;
            Interlocked.Exchange(ref _reconnecting, 0);
        }
    }

    private static int GetExchangeType(string exchange)
    {
        return exchange.ToUpperInvariant() switch
        {
            "NSE" => 1,
            "BSE" => 3,
            _ => throw new InvalidOperationException($"Unknown exchange {exchange}"),
        };
    }

    private async Task HandleTextMessage(ReadOnlyMemory<byte> message)
    {
        var text = Encoding.UTF8.GetString(message.Span);

        if (text.Equals("pong", StringComparison.OrdinalIgnoreCase))
        {
            _lastPong = DateTime.UtcNow;
        }
    }

    private async Task HandleBinaryMessage(ReadOnlyMemory<byte> message)
    {
        var tick = _parser.Parse(message.Span);

        if (tick == null)
            return;

        tick.LastUpdated = DateTime.UtcNow;
        var knownStock = _watchList.Get(tick.SymbolToken);
        if (knownStock != null)
            tick.TradingSymbol = knownStock.Symbol;
        _rawMarketData.Record(tick);

        _cache.Update(tick);

        _watchList.UpdateTick(tick);

        TickReceived?.Invoke(this, tick);
    }
}
