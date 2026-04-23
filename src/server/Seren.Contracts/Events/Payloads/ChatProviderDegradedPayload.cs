using TypeGen.Core.TypeAnnotations;

namespace Seren.Contracts.Events.Payloads;

/// <summary>
/// Informational broadcast emitted when the chat pipeline transparently
/// retries the chosen model after a transient hiccup. Does <b>not</b>
/// close the stream — an <c>output:chat:end</c> still follows once the
/// successful attempt completes. Clients that don't know this event
/// type ignore it silently; clients that do show a non-blocking notice
/// ("Nouvelle tentative sur kimi-k2.6…") so the user understands
/// why the response starts with a pause.
/// </summary>
/// <remarks>
/// Seren deliberately does not cascade to a different model: the user's
/// chosen provider is respected. <see cref="From"/> and <see cref="To"/>
/// will therefore be equal in practice — the shape stays richer to
/// keep the door open for a future cross-model fallback without
/// breaking the wire contract.
/// </remarks>
[ExportTsClass]
public sealed record ChatProviderDegradedPayload
{
    /// <summary>Provider id that failed (e.g. <c>ollama/kimi-k2.6:cloud</c>).</summary>
    public required string From { get; init; }

    /// <summary>Provider id being tried next. Same as <see cref="From"/> when the
    /// pipeline is retrying the same model before cascading.</summary>
    public required string To { get; init; }

    /// <summary>Machine-readable reason the previous attempt failed.</summary>
    /// <remarks>
    /// Values mirror <c>ChatStreamOutcome</c>: <c>"idle_timeout"</c>,
    /// <c>"total_timeout"</c>, <c>"error"</c>. Clients can surface different
    /// wording per reason (fr: "Pas de réponse" vs "Modèle a crashé").
    /// </remarks>
    public required string Reason { get; init; }

    /// <summary>1-indexed attempt number that just failed. Useful for the UI
    /// to show "tentative 2/3" when the user wonders how long the recovery
    /// will take.</summary>
    public required int Attempt { get; init; }
}
