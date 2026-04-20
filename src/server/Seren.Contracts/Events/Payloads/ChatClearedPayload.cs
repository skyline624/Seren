using TypeGen.Core.TypeAnnotations;

namespace Seren.Contracts.Events.Payloads;

/// <summary>
/// Payload of an <c>output:chat:cleared</c> event broadcast to every
/// connected peer when an operator triggers a session reset. The OpenClaw
/// session key is unchanged (so device pairing and long-term memory
/// persist), but the LLM context is wiped and a fresh transcript file is
/// started upstream.
/// </summary>
[ExportTsClass]
public sealed record ChatClearedPayload
{
    /// <summary>Unix epoch milliseconds when the reset was processed.</summary>
    public long At { get; init; }
}
