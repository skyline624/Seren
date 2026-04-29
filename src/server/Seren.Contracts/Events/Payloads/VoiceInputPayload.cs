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

    /// <summary>
    /// Optional client-minted id used as the messageId for the eventual
    /// <c>output:chat:user</c> echo. Letting the client mint it allows the
    /// UI to render an optimistic placeholder bubble immediately and then
    /// reconcile it with the server-transcribed text when the echo arrives,
    /// matching the text-input UX (cf. <see cref="TextInputPayload"/>).
    /// </summary>
    public string? ClientMessageId { get; init; }

    /// <summary>
    /// Optional STT engine override (<c>"parakeet"</c> or <c>"whisper"</c>).
    /// When <c>null</c> the server uses its configured default. Unknown
    /// names fall back to the default with a warning log. Set by the UI
    /// from the <c>seren/voxmind/sttEngine</c> persisted preference.
    /// </summary>
    public string? SttEngine { get; init; }

    /// <summary>
    /// Optional STT language override (ISO 639-1, e.g. <c>"fr"</c> /
    /// <c>"en"</c>). When <c>null</c>, empty, or <c>"auto"</c>, the engine
    /// uses its configured default — for Whisper that means sherpa-onnx
    /// auto-detection. Set by the UI from the
    /// <c>seren/voice/sttLanguage</c> persisted preference.
    /// </summary>
    public string? SttLanguage { get; init; }
}
