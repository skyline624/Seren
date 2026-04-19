using TypeGen.Core.TypeAnnotations;

namespace Seren.Contracts.Events.Payloads;

/// <summary>
/// Payload of an <c>output:approval:request</c> event broadcast by the hub
/// when OpenClaw asks an operator to approve a pending exec command or a
/// plugin action. Covers both <c>exec.approval.requested</c> and
/// <c>plugin.approval.requested</c> upstream events.
/// </summary>
[ExportTsClass]
public sealed record ApprovalRequestPayload
{
    /// <summary>Upstream approval identifier — echo it back when resolving.</summary>
    public required string Id { get; init; }

    /// <summary>"exec" for shell commands, "plugin" for plugin actions.</summary>
    public required string Kind { get; init; }

    /// <summary>Human-readable title of the request (e.g. command display name).</summary>
    public required string Title { get; init; }

    /// <summary>Optional descriptive summary the UI can show alongside the title.</summary>
    public string? Summary { get; init; }

    /// <summary>Optional raw command string, when <see cref="Kind"/> is "exec".</summary>
    public string? Command { get; init; }

    /// <summary>Unix epoch milliseconds when the upstream request was created.</summary>
    public long? CreatedAtMs { get; init; }

    /// <summary>Unix epoch milliseconds after which the approval auto-expires.</summary>
    public long? ExpiresAtMs { get; init; }

    /// <summary>Optional origin channel (e.g. "discord", "slack") when the request was triggered from a chat integration.</summary>
    public string? SourceChannel { get; init; }
}
