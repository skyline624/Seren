using TypeGen.Core.TypeAnnotations;

namespace Seren.Contracts.Events.Payloads;

/// <summary>
/// Payload of an <c>audio:playback:chunk</c> event broadcast by the hub
/// containing a chunk of synthesized audio for playback.
/// </summary>
[ExportTsClass]
public sealed record AudioPlaybackPayload
{
    /// <summary>Raw audio bytes for this chunk.</summary>
    public required byte[] Audio { get; init; }

    /// <summary>Audio format (e.g. "pcm", "mp3"), or <c>null</c> if unspecified.</summary>
    public string? Format { get; init; }

    /// <summary>Optional character identifier whose voice produced the audio.</summary>
    public string? CharacterId { get; init; }
}
