using TypeGen.Core.TypeAnnotations;

namespace Seren.Contracts.Events.Payloads;

/// <summary>
/// Payload of an <c>input:voice</c> event sent by a client to submit
/// voice audio for transcription and processing.
/// </summary>
[ExportTsClass]
public sealed record VoiceInputPayload
{
    /// <summary>Raw audio bytes from the client microphone.</summary>
    public required byte[] AudioData { get; init; }

    /// <summary>Audio format (e.g. "wav", "mp3", "ogg"). Defaults to "wav".</summary>
    public string Format { get; init; } = "wav";

    /// <summary>
    /// Client-provided conversation identifier. The hub forwards it via
    /// <c>x-openclaw-session-key</c> so multi-turn voice exchanges remain
    /// in the same session. Generated server-side when null.
    /// </summary>
    public Guid? SessionId { get; init; }

    /// <summary>
    /// Optional LLM model identifier to use for this request (e.g.
    /// <c>ollama/qwen3:8b</c>, <c>openai/gpt-4o-mini</c>). Set by a
    /// future Settings UI to override the active character's default
    /// <c>AgentId</c>. Same precedence rules as <see cref="TextInputPayload.Model"/>.
    /// </summary>
    public string? Model { get; init; }
}
