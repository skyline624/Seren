namespace Seren.Modules.VoxMind.Speakers;

/// <summary>
/// Thin abstraction over the underlying ONNX embedding extractor. Letting
/// the speaker service depend on this interface (rather than the native
/// <c>SherpaOnnx.SpeakerEmbeddingExtractor</c>) makes it trivial to swap
/// in a stub extractor in unit tests without dragging the native lib
/// onto the test host.
/// </summary>
public interface ISpeakerEmbeddingExtractor
{
    /// <summary><c>true</c> when the extractor has a model loaded and can produce embeddings.</summary>
    bool IsLoaded { get; }

    /// <summary>
    /// Compute an embedding for a 16 kHz mono float buffer. Returns
    /// <c>null</c> when the extractor is not loaded or when the
    /// underlying library throws (logged at debug level by the impl).
    /// </summary>
    float[]? ExtractFromSamples(float[] samples);
}
