using System.Net.WebSockets;

namespace CodeName399.Kite.API.WebSocket.Interface;

public interface IAngelWebSocketConnection : IAsyncDisposable
{
    WebSocketState State { get; }

    Task ConnectAsync(
    Uri uri,
    Dictionary<string, string> headers,
    CancellationToken cancellationToken = default);

    Task SendAsync(
        ArraySegment<byte> buffer,
        WebSocketMessageType messageType,
        bool endOfMessage,
        CancellationToken cancellationToken = default);

    ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(
    Memory<byte> buffer,
    CancellationToken cancellationToken = default);

    Task CloseAsync(
        CancellationToken cancellationToken = default);
}