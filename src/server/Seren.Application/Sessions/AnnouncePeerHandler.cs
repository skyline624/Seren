using Mediator;
using Microsoft.Extensions.Logging;
using Seren.Contracts.Events.Payloads;
using Seren.Domain.Abstractions;
using Seren.Domain.ValueObjects;

namespace Seren.Application.Sessions;

/// <summary>
/// Handles <see cref="AnnouncePeerCommand"/> by registering the announced identity
/// on the peer in the <see cref="IPeerRegistry"/>.
/// </summary>
public sealed class AnnouncePeerHandler : IRequestHandler<AnnouncePeerCommand, AnnouncedPayload>
{
    private readonly IPeerRegistry _registry;
    private readonly ILogger<AnnouncePeerHandler> _logger;

    public AnnouncePeerHandler(IPeerRegistry registry, ILogger<AnnouncePeerHandler> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public ValueTask<AnnouncedPayload> Handle(AnnouncePeerCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_registry.TryGet(request.PeerId, out var peer) || peer is null)
        {
            throw new InvalidOperationException(
                $"Cannot announce unknown peer '{request.PeerId}'.");
        }

        var identity = new ModuleIdentity(
            Id: request.Payload.Identity.Id,
            PluginId: request.Payload.Identity.PluginId,
            Version: request.Payload.Identity.Version,
            Labels: request.Payload.Identity.Labels);

        var updated = peer.Announce(identity);
        _registry.Update(updated);

        _logger.LogInformation(
            "Peer {PeerId} announced as module {ModuleName} (plugin={PluginId}, instance={InstanceId})",
            request.PeerId, request.Payload.Name, identity.PluginId, identity.Id);

        var response = new AnnouncedPayload
        {
            Identity = request.Payload.Identity,
            Name = request.Payload.Name,
            Index = 0,
        };

        return ValueTask.FromResult(response);
    }
}
