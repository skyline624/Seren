using Seren.Application.Abstractions;

namespace Seren.Infrastructure.Audio;

/// <summary>
/// Generates a plausible viseme track from plain text without any real TTS
/// engine. Used by <see cref="NoOpTtsProvider"/> so the full lipsync
/// pipeline (broadcast + viseme renderer) can be exercised during dev and
/// in tests without requiring an external speech service.
/// </summary>
/// <remarks>
/// <para>
/// The heuristic is intentionally naive: each character is mapped to one of
/// the five canonical mouth visemes (Aa / Ih / Ou / Ee / Oh) based on its
/// closest vowel, and consonants emit a brief silence. Frames are spaced
/// evenly over <see cref="GenerateFromText"/>'s total duration parameter.
/// </para>
/// <para>
/// Swap this out for a real viseme source (Azure Speech callback,
/// ElevenLabs character timestamps, or offline phoneme aligner) when
/// production TTS lands — the downstream contract (<see cref="VisemeFrame"/>)
/// stays the same.
/// </para>
/// </remarks>
public static class SyntheticVisemeGenerator
{
    private const float DefaultMillisPerChar = 70f;

    /// <summary>
    /// Generates a viseme track for <paramref name="text"/>. When
    /// <paramref name="totalDurationMs"/> is null or non-positive, duration
    /// falls back to <see cref="DefaultMillisPerChar"/> × character count
    /// so the track roughly matches natural reading speed.
    /// </summary>
    public static VisemeFrame[] GenerateFromText(string text, float? totalDurationMs = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0)
        {
            return [];
        }

        var duration = totalDurationMs is > 0
            ? totalDurationMs.Value
            : text.Length * DefaultMillisPerChar;

        var msPerChar = duration / text.Length;
        var frameDurationSec = msPerChar / 1000f;

        var frames = new List<VisemeFrame>(capacity: text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var viseme = MapCharToViseme(text[i]);
            var startSec = i * msPerChar / 1000f;
            // Silence characters (punctuation / consonants) get weight 0 so
            // the mouth closes; vowels get a natural 0.8 opening.
            var weight = viseme == "-" ? 0f : 0.8f;
            frames.Add(new VisemeFrame(viseme, startSec, frameDurationSec, weight));
        }

        return [.. frames];
    }

    /// <summary>
    /// Maps a single character to one of the five canonical visemes or the
    /// silence viseme (<c>-</c>). Case-insensitive; unknown characters
    /// (punctuation, digits, whitespace) map to silence.
    /// </summary>
    private static string MapCharToViseme(char c)
    {
        return char.ToLowerInvariant(c) switch
        {
            'a' or 'â' or 'à' or 'ä' => "aa",
            'i' or 'î' or 'ï' or 'y' => "ih",
            'u' or 'û' or 'ü' or 'o' or 'ô' or 'ö' when c is 'u' or 'û' or 'ü' => "ou",
            'o' or 'ô' or 'ö' => "oh",
            'e' or 'é' or 'è' or 'ê' or 'ë' => "ee",
            _ => "-",
        };
    }
}
