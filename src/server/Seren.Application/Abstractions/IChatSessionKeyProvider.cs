namespace Seren.Application.Abstractions;

/// <summary>
/// Exposes the stable session key Seren uses for every chat interaction
/// routed through OpenClaw. A single, server-managed value guarantees that
/// every UI client connected to the same Seren instance shares the same
/// conversation history and live updates, regardless of device.
/// </summary>
/// <remarks>
/// The infrastructure layer binds this from <c>OpenClaw:MainSessionKey</c>;
/// the application layer only ever reads it. Centralising the value here
/// keeps handlers free from the cross-cutting concern of "what's our
/// session?" and lets us swap the strategy later (per-character key,
/// per-user key, …) without touching the chat / voice handlers.
/// </remarks>
public interface IChatSessionKeyProvider
{
    /// <summary>The stable session key sent to OpenClaw on every <c>chat.send</c>.</summary>
    string MainSessionKey { get; }

    /// <summary>
    /// Rotate the session key to start a fresh OpenClaw conversation while
    /// keeping the device pairing and the long-term memory plugins (which
    /// are scoped to the plugin, not to the session id) intact. Returns the
    /// new key.
    /// </summary>
    /// <remarks>
    /// Rotation is the chosen "reset" mechanism because the upstream
    /// <c>sessions.reset</c> RPC requires <c>operator.admin</c>, which a
    /// bootstrap-paired Seren device is not granted by default. Rotation
    /// achieves the same user-visible result without the privilege.
    /// </remarks>
    Task<string> RotateAsync(CancellationToken cancellationToken);
}
