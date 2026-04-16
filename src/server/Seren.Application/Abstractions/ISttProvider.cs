namespace Seren.Application.Abstractions;

/// <summary>
/// Speech-to-text abstraction — transcribes audio data into text.
/// Implemented by the infrastructure layer (DIP).
/// </summary>
public interface ISttProvider
{
    /// <summary>
    /// Transcribes the given audio data into text.
    /// </summary>
    /// <param name="audioData">Raw audio bytes.</param>
    /// <param name="format">Audio format (e.g. "wav", "mp3", "ogg", "webm").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The transcription result.</returns>
    Task<SttResult> TranscribeAsync(byte[] audioData, string format, CancellationToken ct = default);
}
