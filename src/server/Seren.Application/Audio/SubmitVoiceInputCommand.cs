using Mediator;

namespace Seren.Application.Audio;

/// <summary>
/// Command to submit voice input: the handler transcribes the audio,
/// sends the transcribed text to OpenClaw, and streams the LLM response
/// (with optional TTS synthesis) back to connected peers.
/// </summary>
/// <param name="AudioData">Raw audio bytes from the client.</param>
/// <param name="Format">Audio format (e.g. "wav", "mp3", "ogg").</param>
/// <param name="SessionId">Optional session identifier for conversation continuity.</param>
/// <param name="PeerId">Optional peer id of the originating client.</param>
public sealed record SubmitVoiceInputCommand(
    byte[] AudioData,
    string Format = "wav",
    Guid? SessionId = null,
    string? PeerId = null) : ICommand<string>;
