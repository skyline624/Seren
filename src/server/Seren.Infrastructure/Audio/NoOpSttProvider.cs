using Seren.Application.Abstractions;

namespace Seren.Infrastructure.Audio;

/// <summary>
/// No-op fallback for <see cref="ISttProvider"/> when STT is not configured.
/// </summary>
public sealed class NoOpSttProvider : ISttProvider
{
    /// <inheritdoc />
    public Task<SttResult> TranscribeAsync(
        byte[] audioData,
        string format,
        CancellationToken ct = default)
    {
        return Task.FromResult(new SttResult("[STT not configured]"));
    }
}
