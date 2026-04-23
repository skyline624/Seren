using TypeGen.Core.TypeAnnotations;

namespace Seren.Contracts.Events.Payloads;

/// <summary>
/// Payload of an <c>error</c> event sent by the hub to a client.
/// </summary>
[ExportTsClass]
public sealed record ErrorPayload
{
    /// <summary>Human-readable error message.</summary>
    public required string Message { get; init; }

    /// <summary>Optional machine-readable error code.</summary>
    public string? Code { get; init; }

    /// <summary>
    /// Error taxonomy — see <see cref="StreamErrorCategory"/>. Drives the
    /// UI's remediation affordance (Retry button, fallback banner, support
    /// link). Nullable for backward-compat with pre-resilience callers.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// When the error is tied to a specific upstream provider (e.g. a
    /// timeout on <c>ollama/kimi-k2.6:cloud</c>), expose it so the UI can
    /// suggest switching.
    /// </summary>
    public string? FailedProvider { get; init; }
}
