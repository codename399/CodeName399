using CodeName399.Kite.API.Models.WebSocketOrder;

namespace CodeName399.Kite.API.WebSocket.Interface;

public interface IAngelOrderWebSocket
{
    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    bool IsConnected { get; }

    event Action<OrderStatusMessage>? OrderUpdated;
}