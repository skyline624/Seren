using Microsoft.Extensions.Logging;
using SherpaOnnx;

namespace Seren.Modules.VoxMind.Speakers;

/// <summary>
/// sherpa-onnx implementation of <see cref="ISpeakerEmbeddingExtractor"/>.
/// Loads the configured ONNX model lazily on first <see cref="ExtractFromSamples"/>
/// call so the host doesn't pay the cold-load cost during DI graph
/// construction. A per-instance lock serialises native calls — the
/// underlying handle is not safe to share across threads.
/// </summary>
public sealed class SherpaOnnxSpeakerEmbeddingExtractor : ISpeakerEmbeddingExtractor, IDisposable
{
    private readonly string _modelPath;
    private readonly int _numThreads;
    private readonly ILogger<SherpaOnnxSpeakerEmbeddingExtractor> _logger;
    private readonly Lock _gate = new();
    private SpeakerEmbeddingExtractor? _native;
    private bool _initAttempted;
    private bool _disposed;

    public SherpaOnnxSpeakerEmbeddingExtractor(
        string modelPath,
        int numThreads,
        ILogger<SherpaOnnxSpeakerEmbeddingExtractor> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _modelPath = modelPath ?? string.Empty;
        _numThreads = numThreads <= 0 ? 1 : numThreads;
        _logger = logger;
    }

    public bool IsLoaded
    {
        get
        {
            lock (_gate)
            {
                EnsureLoaded();
                return _native is not null;
            }
        }
    }

    public float[]? ExtractFromSamples(float[] samples)
    {
        if (samples is null || samples.Length == 0)
        {
            return null;
        }

        lock (_gate)
        {
            EnsureLoaded();
            if (_native is null)
            {
                return null;
            }

            try
            {
                var stream = _native.CreateStream();
                stream.AcceptWaveform(16000, samples);
                stream.InputFinished();
                return _native.Compute(stream);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Speaker embedding extraction failed.");
                return null;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        lock (_gate)
        {
            _native?.Dispose();
            _native = null;
        }
    }

    private void EnsureLoaded()
    {
        if (_initAttempted)
        {
            return;
        }
        _initAttempted = true;

        if (string.IsNullOrWhiteSpace(_modelPath) || !File.Exists(_modelPath))
        {
            _logger.LogWarning(
                "Speaker embedding model not found at {Path}; speaker recognition is dormant.",
                _modelPath);
            return;
        }

        try
        {
            var cfg = new SpeakerEmbeddingExtractorConfig
            {
                Model = _modelPath,
                NumThreads = _numThreads,
                Debug = 0,
                Provider = "cpu",
            };
            _native = new SpeakerEmbeddingExtractor(cfg);
            _logger.LogInformation(
                "Speaker embedding extractor loaded from {Path} (threads={Threads}).",
                _modelPath, _numThreads);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to load speaker embedding model from {Path}; speaker recognition is dormant.",
                _modelPath);
            _native = null;
        }
    }
}
