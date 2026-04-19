using Seren.Application.Abstractions;
using Seren.Contracts.Events;
using Seren.Domain.ValueObjects;

namespace Seren.Application.Tests.OpenClaw;

/// <summary>
/// Shared in-memory <see cref="ISerenHub"/> fake used across the OpenClaw
/// relay handler tests. Records every broadcast envelope so assertions can
/// verify both the wire event type and the serialized payload.
/// </summary>
internal sealed class FakeSerenHub : ISerenHub
{
    public List<WebSocketEnvelope> BroadcastEnvelopes { get; } = [];

    public Task<bool> SendAsync(PeerId peerId, WebSocketEnvelope envelope, CancellationToken cancellationToken) =>
        Task.FromResult(true);

    public Task<int> BroadcastAsync(WebSocketEnvelope envelope, PeerId? excluding, CancellationToken cancellationToken)
    {
        BroadcastEnvelopes.Add(envelope);
        return Task.FromResult(BroadcastEnvelopes.Count);
    }
}
