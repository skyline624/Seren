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
}
