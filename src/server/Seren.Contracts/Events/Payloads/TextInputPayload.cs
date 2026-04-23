using TypeGen.Core.TypeAnnotations;

namespace Seren.Contracts.Events.Payloads;

/// <summary>
/// Payload of an <c>input:text</c> event sent by a client to submit
/// a text message for processing by the active character.
/// </summary>
[ExportTsClass]
public sealed record TextInputPayload
{
    /// <summary>User's text message.</summary>
    public required string Text { get; init; }

    /// <summary>Optional session identifier for conversation continuity.</summary>
    public Guid? SessionId { get; init; }

    /// <summary>
    /// Optional LLM model identifier to use for this request (e.g.
    /// <c>ollama/qwen3:8b</c>, <c>openai/gpt-4o-mini</c>). Set by a
    /// future Settings UI to override the active character's default
    /// <c>AgentId</c>. Precedence at the handler:
    /// <c>request.Model ?? character.AgentId ?? OpenClawOptions.DefaultAgentId</c>.
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// Client-generated stable id for the optimistic user bubble. The hub
    /// echoes it back via <c>output:chat:user</c> so peer tabs can render
    /// the same message with a consistent id, and the originating tab can
    /// recognise its own echo and skip the duplicate insertion. Optional
    /// for wire compatibility with clients that pre-date the echo
    /// feature — when missing, the hub falls back to a server-generated
    /// GUID and the originating tab won't dedup (acceptable trade-off
    /// since older clients also never see the echo path).
    /// </summary>
    public string? ClientMessageId { get; init; }
}
