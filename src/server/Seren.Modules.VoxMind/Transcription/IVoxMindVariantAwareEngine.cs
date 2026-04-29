using Seren.Application.Abstractions;

namespace Seren.Modules.VoxMind.Transcription;

/// <summary>
/// Optional capability for STT engines that ship multiple bundle
/// variants (e.g. Whisper across <c>tiny / base / small / medium /
/// large</c>) and / or accept a per-request language hint at decode
/// time. The router (<see cref="VoxMindSttProvider"/>) probes for this
/// interface via <c>is</c> and forwards both the variant size and the
/// language hint when the engine implements it; engines that don't
/// (e.g. Parakeet) are dispatched through the plain
/// <see cref="IVoxMindSttEngine.TranscribeAsync(byte[], string, System.Threading.CancellationToken)"/>
/// surface — no special-casing in the router (Open/Closed).
/// </summary>
/// <remarks>
/// Kept distinct from <see cref="IVoxMindSttEngine"/> rather than
/// merged via default interface methods so that:
/// <list type="bullet">
///   <item>Parakeet's contract stays minimal (Interface Segregation).</item>
///   <item>Tests can mock variant-aware behaviour without dragging
///   sherpa-onnx native libs into the test suite.</item>
/// </list>
/// </remarks>
public interface IVoxMindVariantAwareEngine
{
    /// <summary>
    /// Transcribes audio using the requested variant + language.
    /// Implementations cache one recognizer per (variant, language)
    /// pair; switching variants or languages mid-session re-uses the
    /// existing recognizer if it has been loaded.
    /// </summary>
    /// <param name="audioData">Raw audio bytes.</param>
    /// <param name="format">Audio format (e.g. <c>"wav"</c>, <c>"webm"</c>).</param>
    /// <param name="size">
    /// Variant token without the engine prefix (<c>"tiny"</c>,
    /// <c>"small"</c>, …). Empty / unknown values fall back to the
    /// engine's configured default size.
    /// </param>
    /// <param name="language">
    /// ISO 639-1 code to force at decode time (<c>"fr"</c>,
    /// <c>"en"</c>, …) or <c>null</c> / empty to fall back to the
    /// engine's configured default language. <c>"auto"</c> normalisation
    /// is the router's job; engines receive either a real ISO code or
    /// <c>null</c>.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<SttResult> TranscribeAsync(
        byte[] audioData,
        string format,
        string size,
        string? language,
        CancellationToken ct = default);
}
