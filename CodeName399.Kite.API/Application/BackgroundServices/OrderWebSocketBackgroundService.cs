using CodeName399.Kite.API.WebSocket.Interface;

public class OrderWebSocketBackgroundService : BackgroundService
{
    private readonly IAngelOrderWebSocket _socket;

    private readonly ILogger<OrderWebSocketBackgroundService> _logger;

    public OrderWebSocketBackgroundService(
        IAngelOrderWebSocket socket,
        ILogger<OrderWebSocketBackgroundService> logger
    )
    {
        _socket = socket;

        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_socket.IsConnected)
                {
                    await _socket.StartAsync();
                }

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Order websocket failed.");

                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _socket.StopAsync();

        await base.StopAsync(cancellationToken);
    }
}
