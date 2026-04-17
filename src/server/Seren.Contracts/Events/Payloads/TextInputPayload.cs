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
}
