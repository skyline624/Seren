using System.Buffers;
using System.Net.WebSockets;
using System.Text;

namespace Seren.Infrastructure.OpenClaw.Gateway;

/// <summary>
/// Production implementation of <see cref="IGatewaySocket"/> wrapping a
/// real <see cref="ClientWebSocket"/>. Assumes every text frame payload is
/// valid UTF-8 JSON — the gateway protocol guarantees this.
/// </summary>
internal sealed class ClientWebSocketAdapter : IGatewaySocket
{
    // Max single frame size we expect from OpenClaw (maxPayload in hello-ok
    // is typically 512 KB). We grow the rent as needed in ReceiveTextAsync.
    private const int InitialReceiveBufferSize = 16 * 1024;

    private readonly ClientWebSocket _socket;
    private bool _disposed;

    public ClientWebSocketAdapter(ClientWebSocket socket)
    {
        _socket = socket;
    }

    public WebSocketState State => _socket.State;

    public async Task SendTextAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        await _socket.SendAsync(
            bytes,
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<GatewayReceiveResult> ReceiveTextAsync(CancellationToken cancellationToken)
    {
        var rented = ArrayPool<byte>.Shared.Rent(InitialReceiveBufferSize);
        try
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;

            do
            {
                result = await _socket
                    .ReceiveAsync(new ArraySegment<byte>(rented), cancellationToken)
                    .ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return new GatewayReceiveResult(
                        Text: null,
                        IsClose: true,
                        CloseStatus: result.CloseStatus,
                        CloseStatusDescription: result.CloseStatusDescription);
                }

                ms.Write(rented, 0, result.Count);
            }
            while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Text)
            {
                // Binary frames are unexpected on this protocol — surface as empty
                // text so the read loop logs and skips.
                return new GatewayReceiveResult(Text: string.Empty, IsClose: false, null, null);
            }

            var text = Encoding.UTF8.GetString(ms.ToArray());
            return new GatewayReceiveResult(Text: text, IsClose: false, null, null);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public async Task CloseAsync(
        WebSocketCloseStatus closeStatus,
        string? statusDescription,
        CancellationToken cancellationToken)
    {
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                await _socket.CloseAsync(closeStatus, statusDescription, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (WebSocketException)
            {
                // Best-effort close — swallow transport errors so the caller can
                // continue its teardown sequence.
            }
            catch (OperationCanceledException)
            {
                // Ditto — caller already decided to abandon the session.
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }
        _disposed = true;
        _socket.Dispose();
        return ValueTask.CompletedTask;
    }
}
