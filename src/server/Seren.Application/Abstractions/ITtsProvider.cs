namespace Seren.Application.Abstractions;

/// <summary>
/// Text-to-speech abstraction — synthesizes audio from text.
/// Implemented by the infrastructure layer (DIP).
/// </summary>
public interface ITtsProvider
{
    /// <summary>
    /// Synthesizes audio from the given text.
    /// </summary>
    /// <param name="text">The text to synthesize.</param>
    /// <param name="voice">Optional voice identifier (provider-specific).</param>
    /// <param name="language">
    /// Optional ISO 639-1 language code (e.g. <c>"fr"</c>, <c>"en"</c>). Multilingual
    /// engines route the synthesis to the matching checkpoint; cloud providers that
    /// auto-detect from text (OpenAI) ignore this hint. When <c>null</c>, the
    /// implementation falls back to its configured default language.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An async enumerable of audio chunks.</returns>
    IAsyncEnumerable<TtsChunk> SynthesizeAsync(
        string text,
        string? voice = null,
        string? language = null,
        CancellationToken ct = default);

    /// <summary>
    /// Optional pre-load hook: signals the provider that a synthesis call for
    /// the given <paramref name="language"/> is likely imminent and the
    /// underlying engine should be warmed up off-thread.
    /// </summary>
    /// <remarks>
    /// Cloud providers (OpenAI) and no-op fallbacks ignore this hint — the
    /// default implementation completes immediately. Local engine providers
    /// (VoxMind / F5-TTS) override it to load the language-specific ONNX
    /// sessions in parallel with the upstream LLM call, masking the ~2-4 s
    /// cold-load latency behind the LLM stream.
    /// </remarks>
    Task WarmUpAsync(string? language, CancellationToken ct = default) => Task.CompletedTask;
}
