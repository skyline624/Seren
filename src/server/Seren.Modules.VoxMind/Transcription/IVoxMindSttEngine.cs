using Seren.Application.Abstractions;

namespace Seren.Modules.VoxMind.Transcription;

/// <summary>
/// Internal contract for one local STT engine inside the VoxMind module.
/// Implementations wrap a single ONNX model bundle (Parakeet, Whisper, …).
/// The public-facing <see cref="ISttProvider"/> is the router
/// (<c>VoxMindSttProvider</c>) that dispatches to the right engine based
/// on the per-request hint or the configured default.
/// </summary>
/// <remarks>
/// This interface is local to <c>Seren.Modules.VoxMind</c> and intentionally
/// not exposed in <c>Seren.Application/Abstractions/</c> — the choice of
/// engine is a module-internal concern that doesn't belong in the
/// application's public contract surface.
/// </remarks>
public interface IVoxMindSttEngine
{
    /// <summary>
    /// Stable lower-case identifier (<c>"parakeet"</c>, <c>"whisper"</c>, …).
    /// Matches the <c>sttEngine</c> hint sent by the UI and the keys used
    /// in <see cref="VoxMindSttOptions"/>.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// True when the engine has its model bundle on disk and is ready to
    /// run inference. Called by the router to decide whether to route to
    /// this engine or fall back to another. Cheap (no I/O on the hot path
    /// after the first call — engines should cache the result).
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Transcribes the audio. Implementation owns the audio decoding
    /// pipeline (FFmpeg) and any ONNX runtime initialisation.
    /// </summary>
    Task<SttResult> TranscribeAsync(byte[] audioData, string format, CancellationToken ct = default);
}
