using TypeGen.Core.TypeAnnotations;

namespace Seren.Contracts.Events.Payloads;

/// <summary>
/// Payload of an <c>output:approval:resolved</c> event broadcast by the hub
/// when OpenClaw reports that a pending approval has been decided. Covers
/// both <c>exec.approval.resolved</c> and <c>plugin.approval.resolved</c>.
/// </summary>
[ExportTsClass]
public sealed record ApprovalResolvedPayload
{
    /// <summary>Upstream approval identifier — matches the one from the request event.</summary>
    public required string Id { get; init; }

    /// <summary>"exec" or "plugin" — same discriminator as the request payload.</summary>
    public required string Kind { get; init; }

    /// <summary>"allow" or "deny" (or any future upstream decision literal).</summary>
    public required string Decision { get; init; }

    /// <summary>Identifier of the operator or automation that resolved the approval.</summary>
    public string? ResolvedBy { get; init; }

    /// <summary>Unix epoch milliseconds when the decision was recorded upstream.</summary>
    public long? ResolvedAtMs { get; init; }
}
