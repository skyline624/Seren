namespace Seren.Application.Characters.Personas;

/// <summary>
/// Canonical meter name for persona-workspace-write telemetry.
/// Declared in Application so the composition root subscribes to the
/// meter without referencing the Infrastructure implementation.
/// </summary>
public static class PersonaWriterMeter
{
    public const string Name = "seren.persona";
}

/// <summary>
/// Instrumentation hook for <see cref="Seren.Application.Abstractions.IPersonaWorkspaceWriter"/>.
/// Single semantic event: a write completed (success or typed failure).
/// </summary>
/// <remarks>
/// Dashboards break down activations by outcome + character name to
/// catch permission / disk-full / misconfigured-workspace regressions
/// before users notice the avatar "forgetting" who they talked to.
/// </remarks>
public interface IPersonaWriterMetrics
{
    /// <summary>Record a persona-write attempt.</summary>
    /// <param name="outcome">One of: <c>"ok"</c>, <c>"error"</c>,
    /// <c>"no_workspace"</c> (silent no-op when the feature is
    /// intentionally disabled).</param>
    /// <param name="characterName">Display name of the character; kept
    /// low-cardinality in practice (a handful of personas per user).</param>
    /// <param name="bytesWritten">Sum of IDENTITY.md + SOUL.md bytes on
    /// success, <c>0</c> otherwise.</param>
    /// <param name="elapsed">End-to-end wall-time of the call.</param>
    void RecordWrite(string outcome, string characterName, long bytesWritten, TimeSpan elapsed);
}
