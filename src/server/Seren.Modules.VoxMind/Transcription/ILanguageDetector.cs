namespace Seren.Modules.VoxMind.Transcription;

/// <summary>
/// Post-hoc language detector applied to transcribed text. Parakeet v3
/// transcribes faithfully in the spoken language but does not surface the
/// detected language code, so we infer it from the produced text.
/// </summary>
/// <remarks>
/// Implementations must be deterministic and side-effect-free so they can be
/// safely registered as a <c>Singleton</c>. Local to the VoxMind module —
/// other STT providers (cloud) typically already return a language code in
/// their response and do not need this surface.
/// </remarks>
public interface ILanguageDetector
{
    /// <summary>
    /// Detects the language of <paramref name="text"/>.
    /// </summary>
    /// <param name="text">Transcribed text (may be empty).</param>
    /// <param name="candidateCodes">
    /// When not <c>null</c>, restricts the prediction to these ISO 639-1 codes.
    /// Useful to bound the detection to the languages the upstream STT engine
    /// supports (Parakeet v3 covers 25 European languages).
    /// </param>
    /// <returns>
    /// ISO 639-1 code (<c>"fr"</c>, <c>"en"</c>, …) or <c>"und"</c> when the
    /// confidence is insufficient (text too short, no overlap).
    /// </returns>
    string DetectLanguage(string text, IEnumerable<string>? candidateCodes = null);
}
