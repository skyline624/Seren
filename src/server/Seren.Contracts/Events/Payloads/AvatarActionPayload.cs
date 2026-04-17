using TypeGen.Core.TypeAnnotations;

namespace Seren.Contracts.Events.Payloads;

/// <summary>
/// Payload of an <c>avatar:action</c> event broadcast by the hub when
/// the LLM output contains an action marker (e.g. <c>&lt;action:wave&gt;</c>).
/// Distinct from <see cref="AvatarEmotionPayload"/>: emotions drive facial
/// expressions, actions drive body gestures (wave, nod, bow, think, …).
/// </summary>
[ExportTsClass]
public sealed record AvatarActionPayload
{
    /// <summary>Action name extracted from the LLM output (e.g. "wave", "nod").</summary>
    public required string Action { get; init; }

    /// <summary>Optional character identifier whose avatar should perform the action.</summary>
    public string? CharacterId { get; init; }
}
