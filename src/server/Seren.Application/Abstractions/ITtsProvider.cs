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
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An async enumerable of audio chunks.</returns>
    IAsyncEnumerable<TtsChunk> SynthesizeAsync(string text, string? voice = null, CancellationToken ct = default);
}
