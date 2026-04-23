using System.Diagnostics.Metrics;
using Seren.Application.Characters.Personas;

namespace Seren.Infrastructure.Characters;

/// <summary>
/// OpenTelemetry-backed implementation of both
/// <see cref="IPersonaWriterMetrics"/> and
/// <see cref="IPersonaCaptureMetrics"/>. Owns a single
/// <see cref="Meter"/> named <see cref="PersonaWriterMeter.Name"/>
/// (<c>seren.persona</c>) — one meter, two semantic events (writes +
/// captures) — so operators subscribe once and read both dashboards
/// off the same pipeline.
/// </summary>
/// <remarks>
/// Cardinality :
/// <list type="bullet">
/// <item><description>writes : <c>outcome</c> ∈ {ok, error, no_workspace}
/// × <c>character</c> (handful of personas).</description></item>
/// <item><description>captures : <c>outcome</c> ∈ {ok, workspace_empty,
/// no_workspace_configured, invalid_persona}. No character tag — on
/// failure we have no valid name anyway.</description></item>
/// </list>
/// </remarks>
public sealed class OtelPersonaMetrics : IPersonaWriterMetrics, IPersonaCaptureMetrics, IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<long> _writes;
    private readonly Counter<long> _bytes;
    private readonly Histogram<double> _duration;
    private readonly Counter<long> _captures;

    public OtelPersonaMetrics()
    {
        _meter = new Meter(PersonaWriterMeter.Name);

        _writes = _meter.CreateCounter<long>(
            name: "seren.persona.writes_total",
            unit: "{activation}",
            description: "Count of persona-workspace-write attempts by outcome and character.");

        _bytes = _meter.CreateCounter<long>(
            name: "seren.persona.bytes_written_total",
            unit: "By",
            description: "Cumulative bytes written to the OpenClaw workspace by the persona writer.");

        _duration = _meter.CreateHistogram<double>(
            name: "seren.persona.write.duration",
            unit: "ms",
            description: "End-to-end duration of a persona-workspace write.");

        _captures = _meter.CreateCounter<long>(
            name: "seren.persona.captures_total",
            unit: "{capture}",
            description: "Count of workspace-persona capture attempts by outcome.");
    }

    public void RecordWrite(string outcome, string characterName, long bytesWritten, TimeSpan elapsed)
    {
        ArgumentException.ThrowIfNullOrEmpty(outcome);
        ArgumentException.ThrowIfNullOrEmpty(characterName);

        _writes.Add(
            1,
            new KeyValuePair<string, object?>("outcome", outcome),
            new KeyValuePair<string, object?>("character", characterName));

        if (bytesWritten > 0)
        {
            _bytes.Add(
                bytesWritten,
                new KeyValuePair<string, object?>("character", characterName));
        }

        _duration.Record(
            elapsed.TotalMilliseconds,
            new KeyValuePair<string, object?>("outcome", outcome));
    }

    public void RecordCapture(string outcome)
    {
        ArgumentException.ThrowIfNullOrEmpty(outcome);
        _captures.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
    }

    public void Dispose() => _meter.Dispose();
}
