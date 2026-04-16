using TypeGen.Core.TypeAnnotations;

namespace Seren.Contracts.Events.Payloads;

/// <summary>
/// Payload of an <c>avatar:emotion</c> event broadcast by the hub
/// when the LLM output contains an emotion marker.
/// </summary>
[ExportTsClass]
public sealed record AvatarEmotionPayload
{
    /// <summary>Emotion name extracted from the LLM output (e.g. "joy", "sadness").</summary>
    public required string Emotion { get; init; }

    /// <summary>Optional character identifier whose avatar should express the emotion.</summary>
    public string? CharacterId { get; init; }
}
