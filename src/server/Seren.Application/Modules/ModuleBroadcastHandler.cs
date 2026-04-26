using System.Text.Json;
using Mediator;
using Microsoft.Extensions.Logging;
using Seren.Application.Abstractions;
using Seren.Contracts.Events;

namespace Seren.Application.Modules;

/// <summary>
/// Generic Mediator handler that translates any <typeparamref name="TBroadcast"/>
/// into a <see cref="WebSocketEnvelope"/> and forwards it through
/// <see cref="ISerenHub"/>. Concrete modules should subclass this with a
/// closed type so the Mediator source generator picks it up — the body
/// stays empty in the leaf class.
/// </summary>
/// <remarks>
/// <para>
/// Boilerplate is minimal: a leaf handler is a one-line constructor passthrough.
/// </para>
/// <code>
/// public sealed class MyNotificationToUiHandler : ModuleBroadcastHandler&lt;MyNotification&gt;
/// {
///     public MyNotificationToUiHandler(ISerenHub hub, ILogger&lt;MyNotificationToUiHandler&gt; logger)
///         : base(hub, logger) { }
/// }
/// </code>
/// <para>
/// The generic base centralises the envelope-building, camelCase serialisation
/// policy and the broadcast call so every module's relay handler shares the
/// same wire shape — DRY, single point of change for the protocol metadata.
/// </para>
/// </remarks>
public abstract class ModuleBroadcastHandler<TBroadcast> : INotificationHandler<TBroadcast>
    where TBroadcast : IModuleBroadcast
{
    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly ModuleIdentityDto BroadcastSource = new()
    {
        Id = "seren-module-broadcast",
        PluginId = "seren",
    };

    private readonly ISerenHub _hub;
    private readonly ILogger _logger;

    protected ModuleBroadcastHandler(ISerenHub hub, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(logger);
        _hub = hub;
        _logger = logger;
    }

    public async ValueTask Handle(TBroadcast notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var envelope = BuildEnvelope(notification.EventType, notification.Payload);

        _logger.LogDebug(
            "Relaying module broadcast {EventType} to UI peers (excluding {ExcludedPeer}).",
            notification.EventType,
            notification.ExcludingPeer?.Value ?? "<none>");

        await _hub.BroadcastAsync(envelope, notification.ExcludingPeer, cancellationToken)
            .ConfigureAwait(false);
    }

    private static WebSocketEnvelope BuildEnvelope(string eventType, object payload)
    {
        var json = JsonSerializer.Serialize(payload, payload.GetType(), CamelCase);
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.Clone();

        return new WebSocketEnvelope
        {
            Type = eventType,
            Data = data,
            Metadata = new EventMetadata
            {
                Source = BroadcastSource,
                Event = new EventIdentity { Id = Guid.NewGuid().ToString("N") },
            },
        };
    }
}
