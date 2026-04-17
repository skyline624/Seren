using Seren.Application.Abstractions;

namespace Seren.Infrastructure.Audio;

/// <summary>
/// No-op fallback for <see cref="ITtsProvider"/> when no real engine is
/// configured. Still emits a <see cref="VisemeFrame"/> track so the
/// client-side lipsync pipeline can be exercised end-to-end in dev.
/// Audio is empty (<c>Format = "none"</c>) — the viewer skips playback
/// but still schedules mouth animations.
/// </summary>
public sealed class NoOpTtsProvider : ITtsProvider
{
    /// <inheritdoc />
    public async IAsyncEnumerable<TtsChunk> SynthesizeAsync(
        string text,
        string? voice = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        var visemes = SyntheticVisemeGenerator.GenerateFromText(text);
        yield return new TtsChunk(Audio: [], Format: "none", Visemes: visemes);
    }
}
