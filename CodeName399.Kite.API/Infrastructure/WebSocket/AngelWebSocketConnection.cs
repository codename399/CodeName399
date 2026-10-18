using CodeName399.Kite.API.WebSocket.Interface;
using System.Net.WebSockets;

namespace CodeName399.Kite.API.Services;

public sealed class AngelWebSocketConnection : IAngelWebSocketConnection
{
    private ClientWebSocket? _socket;

    public WebSocketState State => _socket?.State ?? WebSocketState.None;

    public async Task ConnectAsync(
        Uri uri,
        Dictionary<string, string> headers,
        CancellationToken cancellationToken = default
    )
    {
        _socket?.Dispose();

        _socket = new ClientWebSocket();

        _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

        foreach (var header in headers)
        {
            _socket.Options.SetRequestHeader(header.Key, header.Value);
        }

        await _socket.ConnectAsync(uri, cancellationToken);
    }

    public async Task SendAsync(
        ArraySegment<byte> buffer,
        WebSocketMessageType messageType,
        bool endOfMessage,
        CancellationToken cancellationToken = default
    )
    {
        await _socket.SendAsync(buffer, messageType, endOfMessage, cancellationToken);
    }

    public ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default
    )
    {
        return _socket.ReceiveAsync(buffer, cancellationToken);
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_socket?.State == WebSocketState.Open)
        {
            await _socket.CloseAsync(
                WebSocketCloseStatus.NormalClosure,
                "Closed",
                cancellationToken
            );
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_socket?.State == WebSocketState.Open)
        {
            await CloseAsync();
        }

        _socket?.Dispose();
        _socket = null;
    }
}
