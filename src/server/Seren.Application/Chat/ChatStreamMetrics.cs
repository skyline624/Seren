using System.Diagnostics.Metrics;

namespace Seren.Application.Chat;

/// <summary>
/// OpenTelemetry instrumentation for the chat-stream pipeline. Owns a
/// <see cref="Meter"/> named <c>"seren.chat"</c>; the Server.Api composition
/// root registers that meter with <c>.WithMetrics(m =&gt; m.AddMeter("seren.chat"))</c>
/// so the values flow to whatever OTLP collector is configured.
/// </summary>
/// <remarks>
/// Three instruments cover the SLO + failure-mode analysis needs:
/// <list type="bullet">
///   <item><term>stream.duration</term>
///     <description>Histogram (ms) of full pipeline wall-time, tagged by
///     provider + outcome. Feeds p50/p95/p99 per provider for latency SLOs.</description></item>
///   <item><term>stream.outcome</term>
///     <description>Counter tagged by provider + outcome + attempts_bucket
///     (1 / 2 / 3plus). Gives the "how often do we retry / fall back" view.</description></item>
///   <item><term>stream.fallback</term>
///     <description>Counter tagged by from_provider + to_provider + reason.
///     Pinpoints which primary→fallback transitions happen in production.</description></item>
/// </list>
/// Tag cardinality is acceptable for current deployments (≤ dozens of
/// providers). If OpenRouter/alike blow up cardinality past hundreds,
/// whitelist-by-provider is the intended escape hatch.
/// </remarks>
public sealed class ChatStreamMetrics : IDisposable
{
    public const string MeterName = "seren.chat";

    private readonly Meter _meter;
    private readonly Histogram<double> _durationMs;
    private readonly Counter<long> _outcome;
    private readonly Counter<long> _fallback;

    public ChatStreamMetrics()
    {
        // Meter name matches what OpenTelemetry's `.WithMetrics(m =>
        // m.AddMeter("seren.chat"))` subscribes to in Program.cs. Using
        // `new Meter(...)` directly (instead of IMeterFactory) keeps the
        // dependency surface minimal — instruments are registered globally
        // and the OTel hookup is name-based, not factory-based.
        _meter = new Meter(MeterName);

        _durationMs = _meter.CreateHistogram<double>(
            name: "seren.chat.stream.duration",
            unit: "ms",
            description: "End-to-end chat stream duration from StartAsync to terminal broadcast.");

        _outcome = _meter.CreateCounter<long>(
            name: "seren.chat.stream.outcome",
            unit: "{stream}",
            description: "Count of chat streams by final outcome and number of attempts.");

        _fallback = _meter.CreateCounter<long>(
            name: "seren.chat.stream.fallback",
            unit: "{transition}",
            description: "Count of provider-to-provider transitions triggered by the resilience policy.");
    }

    /// <summary>Records the total duration of a chat stream.</summary>
    public void RecordDuration(TimeSpan elapsed, string provider, string outcome)
    {
        ArgumentException.ThrowIfNullOrEmpty(provider);
        ArgumentException.ThrowIfNullOrEmpty(outcome);

        _durationMs.Record(
            elapsed.TotalMilliseconds,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("outcome", outcome));
    }

    /// <summary>Records the final outcome of a chat stream.</summary>
    public void RecordOutcome(string provider, string outcome, int attempts)
    {
        ArgumentException.ThrowIfNullOrEmpty(provider);
        ArgumentException.ThrowIfNullOrEmpty(outcome);

        var attemptsBucket = attempts switch
        {
            <= 1 => "1",
            2 => "2",
            _ => "3plus",
        };

        _outcome.Add(
            1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("outcome", outcome),
            new KeyValuePair<string, object?>("attempts", attemptsBucket));
    }

    /// <summary>Records a provider-to-provider transition (retry or fallback).</summary>
    public void RecordFallback(string fromProvider, string toProvider, string reason)
    {
        ArgumentException.ThrowIfNullOrEmpty(fromProvider);
        ArgumentException.ThrowIfNullOrEmpty(toProvider);
        ArgumentException.ThrowIfNullOrEmpty(reason);

        _fallback.Add(
            1,
            new KeyValuePair<string, object?>("from_provider", fromProvider),
            new KeyValuePair<string, object?>("to_provider", toProvider),
            new KeyValuePair<string, object?>("reason", reason));
    }

    public void Dispose() => _meter.Dispose();
}
