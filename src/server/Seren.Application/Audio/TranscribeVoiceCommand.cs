using Mediator;

namespace Seren.Application.Audio;

/// <summary>
/// Command to transcribe an audio blob without invoking the chat pipeline.
/// The handler runs the configured <see cref="Abstractions.ISttProvider"/>
/// and unicasts the transcribed text back to the originating peer via
/// <c>output:voice:transcript</c>. Used by the UI "dictate into text input"
/// flow where the user reviews the transcription before pressing Send.
/// </summary>
/// <param name="AudioData">Raw audio bytes captured by the client.</param>
/// <param name="Format">Audio format (e.g. "wav", "mp3", "ogg").</param>
/// <param name="PeerId">Originating peer id; the transcript reply is unicast to it.</param>
/// <param name="RequestId">
/// Optional client-minted correlation id, echoed back so the UI can resolve
/// the matching in-flight transcription promise.
/// </param>
/// <param name="SttEngine">
/// Optional engine name override (<c>"parakeet"</c> / <c>"whisper"</c>)
/// forwarded to the multi-engine STT router. Null = server default.
/// </param>
/// <param name="SttLanguage">
/// Optional ISO 639-1 language hint forwarded to the STT engine. Same
/// semantics as <see cref="SubmitVoiceInputCommand.SttLanguage"/>.
/// </param>
public sealed record TranscribeVoiceCommand(
    byte[] AudioData,
    string Format = "wav",
    string? PeerId = null,
    string? RequestId = null,
    string? SttEngine = null,
    string? SttLanguage = null) : ICommand;
