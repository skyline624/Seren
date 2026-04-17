using System.Text.Json;
using Mediator;

namespace Seren.Application.OpenClaw.Notifications;

/// <summary>
/// Catch-all notification emitted for every server-pushed gateway event
/// that is not a heartbeat tick. Downstream handlers filter on
/// <see cref="EventName"/> and deserialize <see cref="Payload"/> to the
/// domain shape they care about (<c>channel:message</c>, <c>cron:fire</c>,
/// <c>tool:progress</c>, …).
/// </summary>
/// <param name="EventName">Gateway event name, e.g. <c>channel:message</c>.</param>
/// <param name="Payload">Raw payload JSON element — may be <c>null</c> when the gateway sends an event with no body.</param>
/// <param name="Seq">Optional gateway sequence number; contiguous for reliable-ordered streams.</param>
public sealed record OpenClawGatewayRawEventNotification(
    string EventName,
    JsonElement? Payload,
    long? Seq) : INotification;
