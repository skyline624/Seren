using Seren.Application.Abstractions;

namespace Seren.Infrastructure.Audio;

/// <summary>
/// No-op fallback for <see cref="ITtsProvider"/> when TTS is not configured.
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
        yield break;
    }
}
