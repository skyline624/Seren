namespace Seren.Application.Abstractions;

/// <summary>
/// A single chunk of synthesized audio, optionally accompanied by viseme data for lip sync.
/// </summary>
/// <param name="Audio">Raw audio bytes for this chunk.</param>
/// <param name="Format">Audio format (e.g. "pcm", "mp3"), or <c>null</c> if unspecified.</param>
/// <param name="Visemes">Viseme frames for lip sync animation, or <c>null</c> if unavailable.</param>
public sealed record TtsChunk(byte[] Audio, string? Format = null, VisemeFrame[]? Visemes = null);
