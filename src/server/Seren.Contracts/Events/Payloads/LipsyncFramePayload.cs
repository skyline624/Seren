using TypeGen.Core.TypeAnnotations;

namespace Seren.Contracts.Events.Payloads;

/// <summary>
/// Payload of an <c>audio:lipsync:frame</c> event broadcast by the hub
/// containing a viseme frame for lip sync animation.
/// </summary>
[ExportTsClass]
public sealed record LipsyncFramePayload
{
    /// <summary>Viseme identifier (e.g. "aa", "O", "E").</summary>
    public required string Viseme { get; init; }

    /// <summary>Start time in seconds relative to the audio start.</summary>
    public required float StartTime { get; init; }

    /// <summary>Duration of this viseme frame in seconds.</summary>
    public required float Duration { get; init; }

    /// <summary>Blend weight between 0 and 1 (default 1).</summary>
    public float Weight { get; init; } = 1f;

    /// <summary>Optional character identifier whose avatar should animate.</summary>
    public string? CharacterId { get; init; }
}
