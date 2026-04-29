using TypeGen.Core.TypeAnnotations;

namespace Seren.Contracts.Events.Payloads;

/// <summary>
/// Payload of an <c>input:voice:transcribe</c> event — STT-only request.
/// The hub runs the audio through the configured STT provider and replies
/// with <c>output:voice:transcript</c> on the same WebSocket connection.
/// No chat-stream is started, no LLM is invoked.
/// </summary>
[ExportTsClass]
public sealed record VoiceTranscribePayload
{
    /// <summary>Raw audio bytes captured by the client microphone.</summary>
    public required byte[] AudioData { get; init; }

    /// <summary>Audio format (e.g. "wav", "mp3", "ogg"). Defaults to "wav".</summary>
    public string Format { get; init; } = "wav";

    /// <summary>
    /// Optional client-minted correlation id. Echoed back in
    /// <see cref="VoiceTranscriptPayload.RequestId"/> so the UI can match a
    /// reply to the correct in-flight transcription (concurrent dictate
    /// presses, multi-tab scenarios).
    /// </summary>
    public string? RequestId { get; init; }

    /// <summary>
    /// Optional STT engine override (<c>"parakeet"</c> or <c>"whisper"</c>).
    /// Same semantics as <see cref="VoiceInputPayload.SttEngine"/>: null =
    /// server default, unknown = warning + default.
    /// </summary>
    public string? SttEngine { get; init; }

    /// <summary>
    /// Optional STT language override (ISO 639-1). Same semantics as
    /// <see cref="VoiceInputPayload.SttLanguage"/>.
    /// </summary>
    public string? SttLanguage { get; init; }
}
