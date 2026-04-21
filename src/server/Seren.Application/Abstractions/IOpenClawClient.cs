namespace Seren.Application.Abstractions;

/// <summary>
/// Application-layer contract for read-only lookups against the OpenClaw
/// gateway (currently model enumeration). Streaming chat completions live
/// on <see cref="IOpenClawChat"/>; this interface is intentionally narrow.
/// </summary>
public interface IOpenClawClient
{
    /// <summary>
    /// Retrieves the list of agent-accessible models currently configured on
    /// the gateway. Implemented on top of the <c>models.list</c> RPC.
    /// </summary>
    Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken ct = default);

    /// <summary>
    /// Asks the gateway to rescan its provider catalogs (e.g. Ollama's
    /// <c>/api/tags</c>) by triggering a SIGUSR1 self-restart via the
    /// <c>POST /tools/invoke</c> HTTP endpoint. Works regardless of whether
    /// OpenClaw runs in Docker, as a systemd service on the host, or
    /// elsewhere — the signal is emitted by OpenClaw to itself.
    /// </summary>
    /// <remarks>
    /// The call returns as soon as the gateway acknowledges the restart
    /// request; callers should expect a short unavailability window
    /// (~3-5 s) while the node process respawns. The fresh catalog becomes
    /// visible on the next <see cref="GetModelsAsync"/> call after the
    /// gateway finishes its new handshake.
    /// </remarks>
    Task RefreshCatalogAsync(CancellationToken ct = default);
}

/// <summary>
/// Metadata about a model available in OpenClaw Gateway.
/// </summary>
public sealed record ModelInfo(string Id, string? Description);
