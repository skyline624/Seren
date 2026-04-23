namespace Seren.Application.Characters.Personas;

/// <summary>
/// Instrumentation hook for <c>CapturePersonaHandler</c>. Single
/// semantic event: a capture attempt completed with an outcome.
/// </summary>
/// <remarks>
/// Dashboards aggregate on <c>outcome</c> to catch misconfigured
/// workspaces, invalid persona markdown, or lucky round-trips in
/// roughly equal parts. Declared alongside <c>IPersonaWriterMetrics</c>
/// because both implementations share the meter name
/// <see cref="PersonaWriterMeter.Name"/> — a single OTEL meter, two
/// counters (writes + captures).
/// </remarks>
public interface IPersonaCaptureMetrics
{
    /// <summary>Record a capture attempt.</summary>
    /// <param name="outcome">One of the
    /// <see cref="Seren.Contracts.Characters.PersonaCaptureError"/>
    /// constants (<c>workspace_empty</c>, <c>invalid_persona</c>,
    /// <c>no_workspace_configured</c>) or <c>"ok"</c> on success.</param>
    void RecordCapture(string outcome);
}
