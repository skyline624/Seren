namespace Seren.Application.Abstractions;

/// <summary>
/// Speech-to-text abstraction — transcribes audio data into text.
/// Implemented by the infrastructure layer (DIP).
/// </summary>
public interface ISttProvider
{
    /// <summary>
    /// Transcribes the given audio data into text using the provider's
    /// default engine.
    /// </summary>
    /// <param name="audioData">Raw audio bytes.</param>
    /// <param name="format">Audio format (e.g. "wav", "mp3", "ogg", "webm").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The transcription result.</returns>
    Task<SttResult> TranscribeAsync(byte[] audioData, string format, CancellationToken ct = default);

    /// <summary>
    /// Transcribes the given audio data with an optional engine hint.
    /// Multi-engine providers (e.g. <c>VoxMindSttProvider</c> with Parakeet +
    /// Whisper) honour the hint to route to a specific local model;
    /// single-engine providers ignore it via the default implementation.
    /// </summary>
    /// <param name="audioData">Raw audio bytes.</param>
    /// <param name="format">Audio format (e.g. "wav", "mp3", "ogg", "webm").</param>
    /// <param name="engineHint">
    /// Caller-requested engine name (e.g. <c>"parakeet"</c>, <c>"whisper"</c>).
    /// When <c>null</c> the provider's configured default engine is used.
    /// Unknown names fall back to the default with a warning log.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The transcription result.</returns>
    Task<SttResult> TranscribeAsync(
        byte[] audioData,
        string format,
        string? engineHint,
        CancellationToken ct = default)
        => TranscribeAsync(audioData, format, ct);

    /// <summary>
    /// Transcribes the given audio with both an engine hint and a
    /// language hint. Multi-engine providers (e.g. <c>VoxMindSttProvider</c>)
    /// honour the language to switch the recognizer's decode language —
    /// notably useful for Whisper, where forcing the language at decode
    /// time gives substantially better results on Romance languages
    /// versus the auto-detect fallback. Single-engine providers ignore
    /// the language via the default implementation below.
    /// </summary>
    /// <param name="audioData">Raw audio bytes.</param>
    /// <param name="format">Audio format (e.g. "wav", "mp3", "ogg", "webm").</param>
    /// <param name="engineHint">
    /// Caller-requested engine name (cf. <see cref="TranscribeAsync(byte[], string, string?, CancellationToken)"/>).
    /// </param>
    /// <param name="languageHint">
    /// Caller-requested ISO 639-1 language code (e.g. <c>"fr"</c>,
    /// <c>"en"</c>). When <c>null</c>, empty, or <c>"auto"</c> the
    /// provider's configured default is used.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The transcription result.</returns>
    Task<SttResult> TranscribeAsync(
        byte[] audioData,
        string format,
        string? engineHint,
        string? languageHint,
        CancellationToken ct = default)
        => TranscribeAsync(audioData, format, engineHint, ct);
}
