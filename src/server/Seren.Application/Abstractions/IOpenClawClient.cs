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
}

/// <summary>
/// Metadata about a model available in OpenClaw Gateway.
/// </summary>
public sealed record ModelInfo(string Id, string? Description);
