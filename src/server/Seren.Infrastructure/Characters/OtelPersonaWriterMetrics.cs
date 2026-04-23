using System.Diagnostics.Metrics;
using Seren.Application.Characters.Personas;

namespace Seren.Infrastructure.Characters;

/// <summary>
/// OpenTelemetry-backed <see cref="IPersonaWriterMetrics"/>. Owns a
/// <see cref="Meter"/> named <see cref="PersonaWriterMeter.Name"/>;
/// Seren.Server.Api wires the meter into the collector via
/// <c>.WithMetrics(m =&gt; m.AddMeter(PersonaWriterMeter.Name))</c>.
/// </summary>
/// <remarks>
/// Cardinality : <c>outcome</c> ∈ {ok, error, no_workspace},
/// <c>character</c> is the display name (low cardinality in practice
/// — a handful of active personas per user). Duration in a histogram,
/// bytes in a counter so aggregation over time gives a "how much
/// churn on the workspace" signal.
/// </remarks>
public sealed class OtelPersonaWriterMetrics : IPersonaWriterMetrics, IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<long> _writes;
    private readonly Counter<long> _bytes;
    private readonly Histogram<double> _duration;

    public OtelPersonaWriterMetrics()
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

    public void Dispose() => _meter.Dispose();
}
