using System.Diagnostics.Metrics;
using Seren.Application.Characters.Import;

namespace Seren.Infrastructure.Characters;

/// <summary>
/// OpenTelemetry-backed <see cref="ICharacterImportMetrics"/>. Owns a
/// <see cref="Meter"/> named <c>"seren.characters"</c>; the Server.Api
/// composition root subscribes to that meter via
/// <c>.WithMetrics(m =&gt; m.AddMeter("seren.characters"))</c> so the
/// values flow to the configured OTLP collector alongside
/// <c>seren.chat</c>.
/// </summary>
/// <remarks>
/// Cardinality of the <c>outcome</c> tag is bounded to the values in
/// <see cref="Seren.Contracts.Characters.CharacterImportError"/> plus
/// <c>"ok"</c> — perfectly safe. <c>spec_version</c> takes one of
/// <c>chara_card_v3</c>, <c>chara_card_v2</c>, or <c>unknown</c>.
/// <c>had_avatar</c> is a bool serialised as <c>"true"</c>/<c>"false"</c>.
/// </remarks>
public sealed class OtelCharacterImportMetrics : ICharacterImportMetrics, IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<long> _outcome;
    private readonly Histogram<double> _duration;

    public OtelCharacterImportMetrics()
    {
        _meter = new Meter(CharacterImportMeter.Name);

        _outcome = _meter.CreateCounter<long>(
            name: "seren.characters.import.outcome",
            unit: "{import}",
            description: "Count of Character Card import attempts by outcome, spec version, and avatar presence.");

        _duration = _meter.CreateHistogram<double>(
            name: "seren.characters.import.duration",
            unit: "ms",
            description: "End-to-end handler wall-time for Character Card imports.");
    }

    public void RecordImport(string outcome, string specVersion, bool hadAvatar, TimeSpan elapsed)
    {
        ArgumentException.ThrowIfNullOrEmpty(outcome);
        ArgumentException.ThrowIfNullOrEmpty(specVersion);

        _outcome.Add(
            1,
            new KeyValuePair<string, object?>("outcome", outcome),
            new KeyValuePair<string, object?>("spec_version", specVersion),
            new KeyValuePair<string, object?>("had_avatar", hadAvatar ? "true" : "false"));

        _duration.Record(
            elapsed.TotalMilliseconds,
            new KeyValuePair<string, object?>("outcome", outcome),
            new KeyValuePair<string, object?>("spec_version", specVersion));
    }

    public void Dispose() => _meter.Dispose();
}
