using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;

namespace Seren.Modules.VoxMind.F5Tts;

/// <summary>
/// F5-TTS engine bound to one language — wraps the three ONNX sessions and
/// the tokenizer. Disposable because it owns <see cref="InferenceSession"/>s.
/// </summary>
/// <remarks>
/// The constructor is pure: it only opens ONNX sessions and binds tokenizer
/// resources. Filesystem side-effects (sidecar discovery, atomic ref-text
/// resolution) live in <see cref="LoadFromDisk"/> so callers can choose to
/// inject a fully-resolved checkpoint in tests.
/// <para>
/// <see cref="SyncRoot"/> exposes a per-engine semaphore so the TTS provider
/// can serialise inferences against each ONNX session independently — two
/// languages can synthesise in parallel because each engine owns its own gate.
/// </para>
/// </remarks>
public sealed class F5LanguageEngine : IDisposable
{
    public string Language { get; }
    public F5TtsTokenizer Tokenizer { get; }
    public F5TtsPreprocessor Preprocessor { get; }
    public F5TtsTransformer Transformer { get; }
    public F5TtsDecoder Decoder { get; }
    public string DefaultReferenceWav { get; }
    public string DefaultReferenceText { get; }

    /// <summary>
    /// Per-engine inference gate. ONNX <see cref="InferenceSession"/> is not
    /// safe under concurrent <c>Run()</c> calls, but two distinct engines
    /// (different languages) can run in parallel.
    /// </summary>
    internal SemaphoreSlim SyncRoot { get; } = new(1, 1);

    private bool _disposed;

    public F5LanguageEngine(F5LanguageCheckpoint checkpoint, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        Language = checkpoint.Language;

        var opts = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        };

        Tokenizer = new F5TtsTokenizer(checkpoint.TokensPath);
        Preprocessor = new F5TtsPreprocessor(checkpoint.PreprocessModelPath, opts);
        Transformer = new F5TtsTransformer(checkpoint.TransformerModelPath, opts);
        Decoder = new F5TtsDecoder(checkpoint.DecodeModelPath, opts);
        DefaultReferenceWav = checkpoint.DefaultReferenceWav;
        DefaultReferenceText = checkpoint.DefaultReferenceText;

        logger?.LogInformation(
            "F5-TTS engine loaded for {Lang}: preprocess={Prep}, transformer={Tx}, decode={Dec}.",
            Language, checkpoint.PreprocessModelPath, checkpoint.TransformerModelPath, checkpoint.DecodeModelPath);
    }

    /// <summary>
    /// Convenience factory that resolves the optional <c>reference.txt</c>
    /// sidecar next to the WAV (lets operators update the voice prompt without
    /// touching <c>appsettings.json</c>) and then constructs the engine.
    /// Sidecar wins over <see cref="F5LanguageCheckpoint.DefaultReferenceText"/>.
    /// </summary>
    public static F5LanguageEngine LoadFromDisk(F5LanguageCheckpoint checkpoint, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        var resolved = ResolveReferenceTextOrDefault(checkpoint, logger);
        if (ReferenceEquals(resolved, checkpoint.DefaultReferenceText))
        {
            return new F5LanguageEngine(checkpoint, logger);
        }

        var hydrated = new F5LanguageCheckpoint
        {
            Language = checkpoint.Language,
            PreprocessModelPath = checkpoint.PreprocessModelPath,
            TransformerModelPath = checkpoint.TransformerModelPath,
            DecodeModelPath = checkpoint.DecodeModelPath,
            TokensPath = checkpoint.TokensPath,
            DefaultReferenceWav = checkpoint.DefaultReferenceWav,
            DefaultReferenceText = resolved,
        };
        return new F5LanguageEngine(hydrated, logger);
    }

    private static string ResolveReferenceTextOrDefault(F5LanguageCheckpoint checkpoint, ILogger? logger)
    {
        var dir = Path.GetDirectoryName(checkpoint.DefaultReferenceWav);
        if (string.IsNullOrEmpty(dir))
        {
            return checkpoint.DefaultReferenceText;
        }

        var sidecar = Path.Combine(dir, "reference.txt");
        if (!File.Exists(sidecar))
        {
            return checkpoint.DefaultReferenceText;
        }

        var text = File.ReadAllText(sidecar).Trim();
        if (string.IsNullOrEmpty(text))
        {
            return checkpoint.DefaultReferenceText;
        }

        logger?.LogInformation(
            "F5-TTS [{Lang}]: reference.txt sidecar used ({Chars} chars).",
            checkpoint.Language, text.Length);
        return text;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        // Drain any in-flight inference before closing native sessions.
        SyncRoot.Wait(TimeSpan.FromSeconds(5));
        try
        {
            Preprocessor.Dispose();
            Transformer.Dispose();
            Decoder.Dispose();
        }
        finally
        {
            SyncRoot.Release();
            SyncRoot.Dispose();
        }
    }
}
