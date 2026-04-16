namespace Seren.Application.Abstractions;

/// <summary>
/// Result of a speech-to-text transcription.
/// </summary>
/// <param name="Text">The transcribed text.</param>
/// <param name="Language">Detected language code (e.g. "en", "ja"), or <c>null</c> if not detected.</param>
/// <param name="Confidence">Confidence score between 0 and 1, or <c>null</c> if unavailable.</param>
public sealed record SttResult(string Text, string? Language = null, float? Confidence = null);
