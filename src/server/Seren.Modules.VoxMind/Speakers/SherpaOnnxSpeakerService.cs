using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Seren.Modules.VoxMind.Diagnostics;
using Seren.Modules.VoxMind.Speakers.Database;

namespace Seren.Modules.VoxMind.Speakers;

/// <summary>
/// Default <see cref="ISpeakerIdentificationService"/> implementation: cosine-
/// similarity matching against an in-memory cache of embeddings sourced from
/// SQLite + on-disk <c>.bin</c> blobs. Auto-enrols a fresh
/// <c>{Prefix}{N}</c> profile when no candidate clears the configured
/// threshold. Single-speaker per utterance — diarisation is out of v1
/// scope.
/// </summary>
/// <remarks>
/// Port of the upstream VoxMind <c>SherpaOnnxSpeakerService</c>, adapted
/// to:
/// <list type="bullet">
/// <item>Seren's <see cref="VoxMindOptions"/> binding (no separate config object);</item>
/// <item>typed <see cref="SpeakerIdentificationOutcome"/> instead of the legacy boolean+confidence pair;</item>
/// <item><see cref="VoxMindMetrics"/> counters / histogram for OTel surfacing.</item>
/// </list>
/// Diarisation, verbal renaming, and the persona-link feature stay out
/// of scope — see the velvet-juggling-ladybug plan for v2/v3 chantiers.
/// </remarks>
public sealed class SherpaOnnxSpeakerService : ISpeakerIdentificationService, IDisposable
{
    private readonly IDbContextFactory<VoxMindSpeakerDbContext> _dbFactory;
    private readonly ISpeakerEmbeddingExtractor _extractor;
    private readonly VoxMindMetrics _metrics;
    private readonly ILogger<SherpaOnnxSpeakerService> _logger;
    private readonly VoxMindSpeakerOptions _options;

    private readonly Dictionary<Guid, List<float[]>> _embeddingCache = new();
    private readonly Lock _cacheLock = new();
    private readonly SemaphoreSlim _initSemaphore = new(1, 1);
    private bool _initialized;
    private bool _disposed;

    public SherpaOnnxSpeakerService(
        IOptions<VoxMindOptions> options,
        IDbContextFactory<VoxMindSpeakerDbContext> dbFactory,
        ISpeakerEmbeddingExtractor extractor,
        VoxMindMetrics metrics,
        ILogger<SherpaOnnxSpeakerService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dbFactory);
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value.Speakers;
        _dbFactory = dbFactory;
        _extractor = extractor;
        _metrics = metrics;
        _logger = logger;

        if (!string.IsNullOrWhiteSpace(_options.EmbeddingsDir))
        {
            Directory.CreateDirectory(_options.EmbeddingsDir);
        }
    }

    public bool IsAvailable => _options.Enabled && _extractor.IsLoaded;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized || !_options.Enabled)
        {
            return;
        }

        await _initSemaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var embeddings = await db.SpeakerEmbeddings
                .Include(e => e.Profile)
                .Where(e => e.Profile.IsActive)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            int loaded = 0;
            foreach (var emb in embeddings)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(emb.FilePath) || !File.Exists(emb.FilePath))
                {
                    continue;
                }

                try
                {
                    var bytes = await File.ReadAllBytesAsync(emb.FilePath, ct).ConfigureAwait(false);
                    var vector = MemoryMarshal.Cast<byte, float>(bytes).ToArray();
                    AddToCache(emb.ProfileId, vector);
                    loaded++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to load speaker embedding {EmbeddingId} from {Path}; skipped.",
                        emb.Id, emb.FilePath);
                }
            }

            _initialized = true;
            _logger.LogInformation(
                "Speaker embedding cache initialised ({Loaded} embeddings across {Profiles} profiles).",
                loaded, _embeddingCache.Count);
        }
        finally
        {
            _initSemaphore.Release();
        }
    }

    public async Task<SpeakerIdentificationResult> IdentifyFromAudioAsync(
        byte[] audioData, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return SpeakerIdentificationResult.NotAvailable;
        }

        var stopwatch = Stopwatch.StartNew();
        var result = SpeakerIdentificationResult.NotAvailable;
        try
        {
            await InitializeAsync(ct).ConfigureAwait(false);

            if (!_extractor.IsLoaded)
            {
                result = SpeakerIdentificationResult.NotAvailable;
                return result;
            }

            var samples = ConvertWavToFloat(audioData);
            var durationSec = samples.Length / 16000d;
            if (durationSec < _options.MinAudioDurationSec)
            {
                result = SpeakerIdentificationResult.AudioTooShort;
                return result;
            }

            var embedding = _extractor.ExtractFromSamples(samples);
            if (embedding is null || embedding.Length == 0)
            {
                result = new SpeakerIdentificationResult(
                    SpeakerIdentificationOutcome.Failed, null, null, 0f);
                return result;
            }

            var (bestSimilarity, bestProfileId) = MatchAgainstCache(embedding);

            if (bestProfileId.HasValue && bestSimilarity >= _options.ConfidenceThreshold)
            {
                var name = await ResolveAndTouchProfileAsync(bestProfileId.Value, ct).ConfigureAwait(false);
                result = new SpeakerIdentificationResult(
                    SpeakerIdentificationOutcome.Identified, bestProfileId, name, bestSimilarity);
                return result;
            }

            // Auto-enrol: name = `{Prefix}{N+1}` where N is the current
            // count of active profiles. The result is reported as
            // Enrolled so the pipeline can tag the bubble accordingly.
            var enrolled = await EnrollAutoAsync(embedding, bestSimilarity, (int)durationSec, ct)
                .ConfigureAwait(false);
            result = new SpeakerIdentificationResult(
                SpeakerIdentificationOutcome.Enrolled, enrolled.Id, enrolled.Name, bestSimilarity);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Speaker identification failed.");
            result = new SpeakerIdentificationResult(
                SpeakerIdentificationOutcome.Failed, null, null, 0f);
            return result;
        }
        finally
        {
            stopwatch.Stop();
            _metrics.SpeakerDurationMs.Record(stopwatch.Elapsed.TotalMilliseconds);
            _metrics.SpeakerRequests.Add(
                1,
                new KeyValuePair<string, object?>("outcome", OutcomeTagFor(result)));
        }
    }

    public async Task<IReadOnlyList<SpeakerProfile>> GetAllProfilesAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var entities = await db.SpeakerProfiles
            .Where(p => p.IsActive)
            .OrderByDescending(p => p.LastSeenAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return entities.Select(MapToDomain).ToList();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _initSemaphore.Dispose();
        if (_extractor is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private (float bestSimilarity, Guid? bestProfileId) MatchAgainstCache(float[] embedding)
    {
        List<KeyValuePair<Guid, float[][]>> snapshot;
        lock (_cacheLock)
        {
            if (_embeddingCache.Count == 0)
            {
                return (0f, null);
            }

            snapshot = _embeddingCache
                .Select(kv => new KeyValuePair<Guid, float[][]>(kv.Key, kv.Value.ToArray()))
                .ToList();
        }

        float bestSimilarity = 0f;
        Guid? bestProfileId = null;

        foreach (var (profileId, vectors) in snapshot)
        {
            float bestForProfile = 0f;
            foreach (var stored in vectors)
            {
                var sim = CosineSimilarity(embedding, stored);
                if (sim > bestForProfile)
                {
                    bestForProfile = sim;
                }
            }

            // Compare against the centroid as well — averaging multiple
            // captures helps recognise a speaker across different
            // recording conditions (mic distance, room).
            if (vectors.Length > 1)
            {
                var centroid = new float[embedding.Length];
                for (int i = 0; i < centroid.Length; i++)
                {
                    float sum = 0f;
                    foreach (var v in vectors)
                    {
                        sum += v[i];
                    }
                    centroid[i] = sum / vectors.Length;
                }
                var centroidSim = CosineSimilarity(embedding, centroid);
                if (centroidSim > bestForProfile)
                {
                    bestForProfile = centroidSim;
                }
            }

            if (bestForProfile > bestSimilarity)
            {
                bestSimilarity = bestForProfile;
                bestProfileId = profileId;
            }
        }

        return (bestSimilarity, bestProfileId);
    }

    private async Task<string?> ResolveAndTouchProfileAsync(Guid profileId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var profile = await db.SpeakerProfiles.FindAsync([profileId], ct).ConfigureAwait(false);
        if (profile is null)
        {
            return null;
        }

        profile.LastSeenAt = DateTime.UtcNow;
        profile.DetectionCount++;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return profile.Name;
    }

    private async Task<(Guid Id, string Name)> EnrollAutoAsync(
        float[] embedding, float initialConfidence, int audioDurationSeconds, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var nextOrdinal = await db.SpeakerProfiles.CountAsync(ct).ConfigureAwait(false) + 1;
        var name = $"{_options.AutoEnrolNamePrefix}{nextOrdinal}";
        var profileId = Guid.NewGuid();
        var embeddingId = Guid.NewGuid();
        var filePath = Path.Combine(
            _options.EmbeddingsDir, $"{profileId}_emb_{embeddingId}.bin");

        Directory.CreateDirectory(_options.EmbeddingsDir);
        var bytes = MemoryMarshal.AsBytes(embedding.AsSpan()).ToArray();
        await File.WriteAllBytesAsync(filePath, bytes, ct).ConfigureAwait(false);

        var now = DateTime.UtcNow;
        db.SpeakerProfiles.Add(new SpeakerProfileEntity
        {
            Id = profileId,
            Name = name,
            CreatedAt = now,
            LastSeenAt = now,
            DetectionCount = 1,
            IsActive = true,
        });
        db.SpeakerEmbeddings.Add(new SpeakerEmbeddingEntity
        {
            Id = embeddingId,
            ProfileId = profileId,
            FilePath = filePath,
            CapturedAt = now,
            InitialConfidence = initialConfidence,
            AudioDurationSeconds = audioDurationSeconds,
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        AddToCache(profileId, embedding);
        _logger.LogInformation(
            "Speaker auto-enrolled as {Name} (id={Id}, similarity={Similarity:F3}).",
            name, profileId, initialConfidence);

        return (profileId, name);
    }

    private void AddToCache(Guid profileId, float[] embedding)
    {
        lock (_cacheLock)
        {
            if (!_embeddingCache.TryGetValue(profileId, out var list))
            {
                list = new List<float[]>();
                _embeddingCache[profileId] = list;
            }
            list.Add(embedding);
        }
    }

    private static SpeakerProfile MapToDomain(SpeakerProfileEntity e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Aliases = string.IsNullOrWhiteSpace(e.AliasesJson)
            ? (IReadOnlyList<string>)Array.Empty<string>()
            : DeserializeAliases(e.AliasesJson) ?? new List<string>(),
        CreatedAt = e.CreatedAt,
        LastSeenAt = e.LastSeenAt,
        DetectionCount = e.DetectionCount,
        IsActive = e.IsActive,
        Notes = e.Notes,
    };

    private static List<string>? DeserializeAliases(string json)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);
        }
        catch
        {
            return null;
        }
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
        {
            return 0f;
        }
        float dot = 0f, normA = 0f, normB = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        return (normA == 0f || normB == 0f) ? 0f : dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
    }

    /// <summary>
    /// Decode a 16-bit little-endian PCM WAV buffer to float32 samples. Mirrors
    /// the upstream parser — handles the standard 44-byte header and falls back
    /// to scanning for the <c>data</c> chunk header when the prelude is non-
    /// canonical (Audacity exports, some mic stacks).
    /// </summary>
    private static float[] ConvertWavToFloat(byte[] wavData)
    {
        if (wavData is null || wavData.Length < 8)
        {
            return Array.Empty<float>();
        }

        int dataOffset = 44;
        var scanLimit = Math.Min(wavData.Length - 8, 512);
        for (int i = 0; i < scanLimit; i++)
        {
            if (wavData[i] == (byte)'d' && wavData[i + 1] == (byte)'a'
                && wavData[i + 2] == (byte)'t' && wavData[i + 3] == (byte)'a')
            {
                dataOffset = i + 8;
                break;
            }
        }

        var n = (wavData.Length - dataOffset) / 2;
        if (n <= 0)
        {
            return Array.Empty<float>();
        }

        var samples = new float[n];
        for (int i = 0; i < n; i++)
        {
            samples[i] = BitConverter.ToInt16(wavData, dataOffset + i * 2) / 32768f;
        }
        return samples;
    }

    private static string OutcomeTagFor(SpeakerIdentificationResult? result) =>
        result?.Outcome switch
        {
            SpeakerIdentificationOutcome.Identified => "identified",
            SpeakerIdentificationOutcome.Enrolled => "enrolled",
            SpeakerIdentificationOutcome.NotEnoughAudio => "not_enough_audio",
            SpeakerIdentificationOutcome.Unavailable => "unavailable",
            SpeakerIdentificationOutcome.Failed => "failed",
            _ => "unknown",
        };
}
