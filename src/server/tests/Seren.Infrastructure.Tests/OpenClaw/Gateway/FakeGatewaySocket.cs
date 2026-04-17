using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Seren.Infrastructure.OpenClaw.Gateway;

namespace Seren.Infrastructure.Tests.OpenClaw.Gateway;

/// <summary>
/// Queue-backed <see cref="IGatewaySocket"/> double. Tests enqueue frames
/// to be received (<see cref="EnqueueServerFrame(string)"/>) and inspect
/// what the SUT sent (<see cref="SentFrames"/>).
/// </summary>
internal sealed class FakeGatewaySocket : IGatewaySocket
{
    private readonly Channel<GatewayReceiveResult> _inbound = Channel.CreateUnbounded<GatewayReceiveResult>(
        new UnboundedChannelOptions { SingleReader = true });

    public WebSocketState State { get; private set; } = WebSocketState.Open;
    public List<string> SentFrames { get; } = new();
    public WebSocketCloseStatus? ClosedWithStatus { get; private set; }
    public string? ClosedWithReason { get; private set; }

    public void EnqueueServerFrame(string json)
        => _inbound.Writer.TryWrite(new GatewayReceiveResult(json, false, null, null));

    public void EnqueueServerClose(WebSocketCloseStatus status = WebSocketCloseStatus.NormalClosure, string? reason = null)
    {
        _inbound.Writer.TryWrite(new GatewayReceiveResult(null, true, status, reason));
        _inbound.Writer.TryComplete();
    }

    public Task SendTextAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SentFrames.Add(Encoding.UTF8.GetString(bytes.Span));
        return Task.CompletedTask;
    }

    public async Task<GatewayReceiveResult> ReceiveTextAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _inbound.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            return new GatewayReceiveResult(null, true, WebSocketCloseStatus.NormalClosure, null);
        }
    }

    public Task CloseAsync(
        WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
    {
        State = WebSocketState.Closed;
        ClosedWithStatus = closeStatus;
        ClosedWithReason = statusDescription;
        _inbound.Writer.TryComplete();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _inbound.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
