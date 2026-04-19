namespace Seren.Infrastructure.OpenClaw.Identity;

/// <summary>
/// Loads or creates Seren's persistent Ed25519 device identity. Called once
/// per gateway session by <c>OpenClawGatewayHandshake</c>; implementations
/// must be safe to call concurrently from a single process.
/// </summary>
public interface IDeviceIdentityStore
{
    /// <summary>
    /// Return the existing identity or create a fresh one and persist it.
    /// The same identity is returned on subsequent calls so the deviceId
    /// stays stable across connections (which is what makes OpenClaw's
    /// paired-device lookup work).
    /// </summary>
    Task<DeviceIdentity> LoadOrCreateAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Persist the marker indicating that the one-shot bootstrap pairing
    /// handshake completed successfully. Subsequent boots will skip the
    /// bootstrap step and go straight to the standard handshake.
    /// </summary>
    Task MarkPairedAsync(CancellationToken cancellationToken);
}
