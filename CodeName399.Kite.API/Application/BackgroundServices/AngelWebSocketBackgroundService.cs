using CodeName399.Kite.API.WebSocket.Interface;

namespace CodeName399.Kite.API.BackgroundServices;

public class AngelWebSocketBackgroundService : BackgroundService
{
    private readonly IAngelWebSocketClient _webSocket;
    private readonly ILogger<AngelWebSocketBackgroundService> _logger;

    public AngelWebSocketBackgroundService(
        IAngelWebSocketClient webSocket,
        ILogger<AngelWebSocketBackgroundService> logger
    )
    {
        _webSocket = webSocket;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_webSocket.IsConnected)
                {
                    await _webSocket.StartAsync(stoppingToken);
                }

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WebSocket disconnected.");

                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }
}
