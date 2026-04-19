using System.Text.Json;
using Mediator;

namespace Seren.Application.OpenClaw.Notifications;

/// <summary>
/// Fired for every OpenClaw <c>agent</c> event (tool lifecycle, item
/// lifecycle, agent stream). The payload is forwarded verbatim as a
/// <see cref="JsonElement"/> — upstream emits many variants and we avoid
/// re-modelling each one server-side. Downstream handlers can filter on
/// <see cref="Stream"/> / <see cref="Phase"/> to react selectively.
/// </summary>
public sealed record AgentEventNotification(
    string RunId,
    string? SessionKey,
    string Stream,
    string? Phase,
    long? Seq,
    JsonElement? Data) : INotification;
