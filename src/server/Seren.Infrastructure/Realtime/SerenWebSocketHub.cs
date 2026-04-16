using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Seren.Application.Abstractions;
using Seren.Contracts.Events;
using Seren.Contracts.Json;
using Seren.Domain.Abstractions;
using Seren.Domain.Entities;
using Seren.Domain.ValueObjects;

namespace Seren.Infrastructure.Realtime;

/// <summary>
/// Default <see cref="ISerenHub"/> implementation. Sends serialized envelopes
/// over the <see cref="WebSocket"/> associated with a given <see cref="PeerId"/>
/// via the internal <see cref="IWebSocketConnectionRegistry"/>.
/// </summary>
public sealed class SerenWebSocketHub : ISerenHub
{
    private readonly IWebSocketConnectionRegistry _connections;
    private readonly IReadOnlyPeerRegistry _peers;
    private readonly ILogger<SerenWebSocketHub> _logger;

    public SerenWebSocketHub(
        IWebSocketConnectionRegistry connections,
        IReadOnlyPeerRegistry peers,
        ILogger<SerenWebSocketHub> logger)
    {
        _connections = connections;
        _peers = peers;
        _logger = logger;
    }

    public async Task<bool> SendAsync(
        PeerId peerId,
        WebSocketEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (!_connections.TryGet(peerId, out var socket) || socket is null)
        {
            _logger.LogDebug(
                "Dropping envelope '{Type}' — peer {PeerId} is not connected.",
                envelope.Type, peerId);
            return false;
        }

        if (socket.State != WebSocketState.Open)
        {
            _logger.LogDebug(
                "Dropping envelope '{Type}' — peer {PeerId} socket is in state {State}.",
                envelope.Type, peerId, socket.State);
            return false;
        }

        try
        {
            var payload = Serialize(envelope);
            await socket.SendAsync(
                payload,
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (WebSocketException ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to send envelope '{Type}' to peer {PeerId}",
                envelope.Type, peerId);
            return false;
        }
    }

    public async Task<int> BroadcastAsync(
        WebSocketEnvelope envelope,
        PeerId? excluding,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var payload = Serialize(envelope);
        var snapshot = _connections.Snapshot();
        var delivered = 0;

        foreach (var (peerId, socket) in snapshot)
        {
            if (excluding is { } excludedPeerId && peerId == excludedPeerId)
            {
                continue;
            }

            if (socket.State != WebSocketState.Open)
            {
                continue;
            }

            if (!_peers.TryGet(peerId, out var peer) || peer is not Peer current || !current.IsAuthenticated)
            {
                continue;
            }

            try
            {
                await socket.SendAsync(
                    payload,
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    cancellationToken).ConfigureAwait(false);
                delivered++;
            }
            catch (WebSocketException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to broadcast envelope '{Type}' to peer {PeerId}",
                    envelope.Type, peerId);
            }
        }

        return delivered;
    }

    private static ArraySegment<byte> Serialize(WebSocketEnvelope envelope)
    {
        var json = JsonSerializer.Serialize(envelope, SerenJsonContext.Default.WebSocketEnvelope);
        var bytes = Encoding.UTF8.GetBytes(json);
        return new ArraySegment<byte>(bytes);
    }
}
