using System.Diagnostics.Metrics;

namespace Seren.Modules.VoxMind.Diagnostics;

/// <summary>
/// OpenTelemetry-compatible metrics surface for the VoxMind module.
/// </summary>
/// <remarks>
/// Registered as a singleton so the underlying <see cref="Meter"/> instances
/// stay alive for the application lifetime. Counters and histograms are
/// auto-collected by the host's <c>MeterProviderBuilder.AddMeter("Seren.VoxMind")</c>
/// in <c>Program.cs</c>; downstream exporters (OTLP, Prometheus, Console) get
/// these series for free.
/// </remarks>
public sealed class VoxMindMetrics : IDisposable
{
    public const string MeterName = "Seren.VoxMind";
    public const string MeterVersion = "1.0.0";

    private readonly Meter _meter;
    public Counter<long> SttRequests { get; }
    public Histogram<double> SttDurationMs { get; }
    public Counter<long> TtsRequests { get; }
    public Histogram<double> TtsDurationMs { get; }
    public Counter<long> TtsCacheHits { get; }
    public Counter<long> TtsCacheMisses { get; }

    private bool _disposed;

    public VoxMindMetrics(IMeterFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _meter = factory.Create(new MeterOptions(MeterName) { Version = MeterVersion });

        SttRequests = _meter.CreateCounter<long>(
            "voxmind.stt.requests",
            unit: "{request}",
            description: "Number of STT transcription requests handled by VoxMind.");

        SttDurationMs = _meter.CreateHistogram<double>(
            "voxmind.stt.duration_ms",
            unit: "ms",
            description: "Wall-clock duration of a VoxMind STT transcription, end-to-end.");

        TtsRequests = _meter.CreateCounter<long>(
            "voxmind.tts.requests",
            unit: "{request}",
            description: "Number of TTS synthesis requests handled by VoxMind.");

        TtsDurationMs = _meter.CreateHistogram<double>(
            "voxmind.tts.duration_ms",
            unit: "ms",
            description: "Wall-clock duration of a VoxMind TTS synthesis, end-to-end.");

        TtsCacheHits = _meter.CreateCounter<long>(
            "voxmind.tts.cache_hits",
            unit: "{event}",
            description: "Number of times a per-language F5 engine was served warm from the LRU cache.");

        TtsCacheMisses = _meter.CreateCounter<long>(
            "voxmind.tts.cache_misses",
            unit: "{event}",
            description: "Number of times a per-language F5 engine had to be cold-loaded from disk.");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _meter.Dispose();
    }
}
