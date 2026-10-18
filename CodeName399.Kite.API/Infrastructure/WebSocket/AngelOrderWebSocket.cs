using CodeName399.Kite.API.Models.WebSocketOrder;
using CodeName399.Kite.API.WebSocket.Interface;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace CodeName399.Kite.API.Services;

public class AngelOrderWebSocket : IAngelOrderWebSocket, IDisposable
{
    private readonly ILogger<AngelOrderWebSocket> _logger;

    private readonly IAngelWebSocketConnection _connection;

    private readonly IAngelOneService _angel;
    private readonly TradingSettingsProvider _settings;
    private readonly OrderStatusCache _cache;
    private TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(Math.Max(1, _settings.Current.WebSocketHeartbeatSeconds));
    private TimeSpan RetryInitialDelay => TimeSpan.FromSeconds(Math.Max(1, _settings.Current.WebSocketRetryInitialSeconds));
    private TimeSpan MaxRetryDelay => TimeSpan.FromSeconds(Math.Max(RetryInitialDelay.TotalSeconds, _settings.Current.WebSocketRetryMaxSeconds));

    private int _currentRetryAttempt;

    private Timer? _heartbeatTimer;

    private CancellationTokenSource? _cts;

    public bool IsConnected => _connection.State == WebSocketState.Open;

    public event Action<OrderStatusMessage>? OrderUpdated;

    public AngelOrderWebSocket(
        ILogger<AngelOrderWebSocket> logger,
        IAngelWebSocketConnection connection,
        IAngelOneService angel,
        TradingSettingsProvider settings,
        OrderStatusCache cache
    )
    {
        _logger = logger;

        _connection = connection;

        _angel = angel;
        _settings = settings;
        _cache = cache;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected)
            return;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var login = await _angel.GetLoginDataAsync();

        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {login.JwtToken}",
        };

        await _connection.ConnectAsync(
            new Uri("wss://tns.angelone.in/smart-order-update"),
            headers,
            _cts.Token
        );

        StartHeartbeat();

        _ = Task.Run(() => ReceiveLoop(_cts.Token));
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _cts?.Cancel();

        _heartbeatTimer?.Dispose();

        _heartbeatTimer = null;

        await _connection.CloseAsync(cancellationToken);
    }

    private async Task ReceiveLoop(CancellationToken token)
    {
        var buffer = new byte[8192];

        while (!token.IsCancellationRequested)
        {
            try
            {
                using var ms = new MemoryStream();

                ValueWebSocketReceiveResult result;

                do
                {
                    result = await _connection.ReceiveAsync(buffer, token);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await ReconnectAsync();

                        return;
                    }

                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    await HandleOrderMessage(ms.ToArray());
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Order ReceiveLoop failed.");

                await ReconnectAsync();

                return;
            }
        }
    }

    private async Task HandleOrderMessage(byte[] bytes)
    {
        string message = Encoding.UTF8.GetString(bytes).Trim();

        if (string.IsNullOrWhiteSpace(message))
            return;

        // Ignore ping / pong messages
        if (
            message.Equals("ping", StringComparison.OrdinalIgnoreCase)
            || message.Equals("pong", StringComparison.OrdinalIgnoreCase)
        )
        {

            return;
        }

        // Ignore anything that isn't JSON
        if (!message.StartsWith("{"))
        {
            return;
        }

        OrderStatusResponse? response;

        try
        {
            response = JsonSerializer.Deserialize<OrderStatusResponse>(
                message,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed parsing Order WebSocket message.");

            _logger.LogError("RAW : {Message}", message);

            return;
        }

        if (response == null)
            return;

        // Angel sends AB00 immediately after connecting.
        if (response.OrderStatusCode == "AB00")
        {
            return;
        }

        if (response.OrderData == null)
            return;

        var order = new OrderStatusMessage
        {
            OrderId = response.OrderData.OrderId,

            Symbol = response.OrderData.TradingSymbol,

            SymbolToken = response.OrderData.SymbolToken,

            Status = response.OrderData.OrderStatus,

            TransactionType = response.OrderData.TransactionType,

            ProductType = response.OrderData.ProductType,

            Variety = response.OrderData.Variety,

            OrderType = response.OrderData.OrderType,

            Exchange = response.OrderData.Exchange,

            AveragePrice = response.OrderData.AveragePrice,

            FilledQuantity = ParseInt(response.OrderData.FilledShares),

            Quantity = ParseInt(response.OrderData.Quantity),

            RejectionReason = response.OrderData.Text,
            OrderTag = response.OrderData.OrderTag,

            UpdatedTime = DateTime.TryParse(response.OrderData.UpdateTime, out var dt)
                ? dt
                : DateTime.UtcNow,
        };

        if (string.Equals(order.Status, "REJECTED", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Order rejected {OrderId} {Reason}",
                order.OrderId,
                order.RejectionReason
            );
        }

        if (string.Equals(order.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Order cancelled {OrderId}", order.OrderId);
        }

        var cached = _cache.Get(order.OrderId);

        if (
            cached != null
            && cached.Status == order.Status
            && cached.FilledQuantity == order.FilledQuantity
            && cached.AveragePrice == order.AveragePrice
        )
        {
            return;
        }

        _cache.Set(order);

        switch (order.Status.ToUpperInvariant())
        {
            case "OPEN":
            case "TRIGGER PENDING":
            case "COMPLETE":
            case "CANCELLED":
            case "REJECTED":
                break;

            default:
                break;
        }

        _currentRetryAttempt = 0;
        OrderUpdated?.Invoke(order);

        await Task.CompletedTask;
    }

    private static int ParseInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        return int.TryParse(value, out var result) ? result : 0;
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
            var bytes = Encoding.UTF8.GetBytes("ping");

            await _connection.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Order websocket ping failed.");

            await ReconnectAsync();
        }
    }

    private async Task ReconnectAsync()
    {
        while (!(_cts?.IsCancellationRequested ?? true))
        {
            _currentRetryAttempt++;
            try
            {
                try { await _connection.CloseAsync(); } catch { }
                var delay = TimeSpan.FromSeconds(Math.Min(MaxRetryDelay.TotalSeconds, RetryInitialDelay.TotalSeconds * Math.Pow(2, Math.Min(_currentRetryAttempt - 1, 6))));
                await Task.Delay(delay, _cts?.Token ?? CancellationToken.None);
                await StartAsync(_cts?.Token ?? CancellationToken.None);
                if (IsConnected) { _currentRetryAttempt = 0; return; }
            }
            catch (OperationCanceledException) when (_cts?.IsCancellationRequested == true) { return; }
            catch (Exception ex) { _logger.LogError(ex, "Order websocket reconnect attempt {Attempt} failed.", _currentRetryAttempt); }
        }
    }

    public void Dispose()
    {
        _heartbeatTimer?.Dispose();
        _cts?.Dispose();
    }
}
