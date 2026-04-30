using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Seren.Modules.VoxMind.Diagnostics;
using Seren.Modules.VoxMind.Speakers;
using Seren.Modules.VoxMind.Speakers.Database;
using Shouldly;
using Xunit;

namespace Seren.Modules.VoxMind.Tests.Speakers;

/// <summary>
/// Unit tests for <see cref="SherpaOnnxSpeakerService"/> exercising the
/// match / auto-enrol / not-enough-audio branches via a stub
/// <see cref="ISpeakerEmbeddingExtractor"/> and an in-memory EF Core
/// context. The native sherpa-onnx libs are not loaded — the service is
/// exercised purely through its logical seams.
/// </summary>
public sealed class SherpaOnnxSpeakerServiceTests
{
    [Fact]
    public async Task FirstUtterance_ShouldEnrollSpeaker1()
    {
        await using var harness = SpeakerServiceHarness.Build(StubExtractor.WithVector(Vector(1f, 0f)));
        var ct = TestContext.Current.CancellationToken;

        var result = await harness.Service.IdentifyFromAudioAsync(WavOf(durationSec: 2), ct);

        result.Outcome.ShouldBe(SpeakerIdentificationOutcome.Enrolled);
        result.SpeakerName.ShouldBe("Speaker_1");
        result.ProfileId.ShouldNotBeNull();
        result.HasSpeaker.ShouldBeTrue();
    }

    [Fact]
    public async Task SecondUtterance_SameVoice_ShouldMatchSameProfile()
    {
        await using var harness = SpeakerServiceHarness.Build(StubExtractor.WithVector(Vector(1f, 0f)));
        var ct = TestContext.Current.CancellationToken;

        var first = await harness.Service.IdentifyFromAudioAsync(WavOf(2), ct);
        var second = await harness.Service.IdentifyFromAudioAsync(WavOf(2), ct);

        first.Outcome.ShouldBe(SpeakerIdentificationOutcome.Enrolled);
        second.Outcome.ShouldBe(SpeakerIdentificationOutcome.Identified);
        second.ProfileId.ShouldBe(first.ProfileId);
        second.SpeakerName.ShouldBe(first.SpeakerName);
    }

    [Fact]
    public async Task DifferentVoice_ShouldEnrollSpeaker2()
    {
        var extractor = new SequencedExtractor(
            Vector(1f, 0f),   // first voice
            Vector(1f, 0f),   // first voice again (matches Speaker_1)
            Vector(0f, 1f));  // second voice (orthogonal — below threshold)
        await using var harness = SpeakerServiceHarness.Build(extractor);
        var ct = TestContext.Current.CancellationToken;

        var first = await harness.Service.IdentifyFromAudioAsync(WavOf(2), ct);
        var sameVoice = await harness.Service.IdentifyFromAudioAsync(WavOf(2), ct);
        var newVoice = await harness.Service.IdentifyFromAudioAsync(WavOf(2), ct);

        first.Outcome.ShouldBe(SpeakerIdentificationOutcome.Enrolled);
        sameVoice.Outcome.ShouldBe(SpeakerIdentificationOutcome.Identified);
        sameVoice.ProfileId.ShouldBe(first.ProfileId);

        newVoice.Outcome.ShouldBe(SpeakerIdentificationOutcome.Enrolled);
        newVoice.SpeakerName.ShouldBe("Speaker_2");
        newVoice.ProfileId.ShouldNotBe(first.ProfileId);
    }

    [Fact]
    public async Task AudioTooShort_ShouldReturnNotEnoughAudioWithoutEnrolling()
    {
        await using var harness = SpeakerServiceHarness.Build(StubExtractor.WithVector(Vector(1f, 0f)));
        var ct = TestContext.Current.CancellationToken;

        // 0.5 s < default MinAudioDurationSec=1.5
        var result = await harness.Service.IdentifyFromAudioAsync(WavOf(durationSec: 0.5), ct);

        result.Outcome.ShouldBe(SpeakerIdentificationOutcome.NotEnoughAudio);
        result.HasSpeaker.ShouldBeFalse();

        var profiles = await harness.Service.GetAllProfilesAsync(ct);
        profiles.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExtractorUnavailable_ShouldReturnUnavailable()
    {
        var extractor = new StubExtractor(loaded: false);
        await using var harness = SpeakerServiceHarness.Build(extractor);
        var ct = TestContext.Current.CancellationToken;

        var result = await harness.Service.IdentifyFromAudioAsync(WavOf(2), ct);

        result.Outcome.ShouldBe(SpeakerIdentificationOutcome.Unavailable);
        result.HasSpeaker.ShouldBeFalse();
    }

    [Fact]
    public async Task ExtractorReturnsNull_ShouldReportFailedOutcome()
    {
        var extractor = new StubExtractor(loaded: true, vector: null);
        await using var harness = SpeakerServiceHarness.Build(extractor);
        var ct = TestContext.Current.CancellationToken;

        var result = await harness.Service.IdentifyFromAudioAsync(WavOf(2), ct);

        result.Outcome.ShouldBe(SpeakerIdentificationOutcome.Failed);
    }

    [Fact]
    public async Task SpeakerDisabled_ShouldShortCircuitToUnavailable()
    {
        await using var harness = SpeakerServiceHarness.Build(
            StubExtractor.WithVector(Vector(1f, 0f)),
            configureOptions: o => o.Speakers.Enabled = false);
        var ct = TestContext.Current.CancellationToken;

        var result = await harness.Service.IdentifyFromAudioAsync(WavOf(2), ct);

        result.Outcome.ShouldBe(SpeakerIdentificationOutcome.Unavailable);
    }

    // ── helpers ────────────────────────────────────────────────────────

    private static float[] Vector(params float[] values) => values;

    /// <summary>Builds a minimal PCM WAV blob whose length encodes the given duration.</summary>
    private static byte[] WavOf(double durationSec)
    {
        // 16 bits * 16 kHz mono * durationSec
        var sampleCount = (int)(16_000 * durationSec);
        var dataBytes = sampleCount * 2;
        var totalLen = 44 + dataBytes;
        var buf = new byte[totalLen];
        // RIFF header (only the `data` chunk header is read by the parser)
        buf[0] = (byte)'R'; buf[1] = (byte)'I'; buf[2] = (byte)'F'; buf[3] = (byte)'F';
        buf[8] = (byte)'W'; buf[9] = (byte)'A'; buf[10] = (byte)'V'; buf[11] = (byte)'E';
        buf[36] = (byte)'d'; buf[37] = (byte)'a'; buf[38] = (byte)'t'; buf[39] = (byte)'a';
        // data chunk size little-endian
        var sizeBytes = BitConverter.GetBytes(dataBytes);
        Buffer.BlockCopy(sizeBytes, 0, buf, 40, 4);
        return buf;
    }
}

/// <summary>
/// Disposable harness building a fully wired
/// <see cref="SherpaOnnxSpeakerService"/> against an in-memory EF Core
/// store and a temp embeddings directory.
/// </summary>
internal sealed class SpeakerServiceHarness : IAsyncDisposable
{
    public required SherpaOnnxSpeakerService Service { get; init; }
    public required InMemoryDbContextFactory Factory { get; init; }
    public required string EmbeddingsDir { get; init; }
    public required VoxMindMetrics Metrics { get; init; }

    public static SpeakerServiceHarness Build(
        ISpeakerEmbeddingExtractor extractor,
        Action<VoxMindOptions>? configureOptions = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "voxmind-speakers-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        var options = new VoxMindOptions();
        options.Speakers.EmbeddingsDir = dir;
        options.Speakers.DbPath = Path.Combine(dir, "speakers.db");
        configureOptions?.Invoke(options);

        var factory = new InMemoryDbContextFactory();
        var metrics = new VoxMindMetrics(new DummyMeterFactory());
        var service = new SherpaOnnxSpeakerService(
            Options.Create(options),
            factory,
            extractor,
            metrics,
            NullLogger<SherpaOnnxSpeakerService>.Instance);

        return new SpeakerServiceHarness
        {
            Service = service,
            Factory = factory,
            EmbeddingsDir = dir,
            Metrics = metrics,
        };
    }

    public async ValueTask DisposeAsync()
    {
        Service.Dispose();
        Metrics.Dispose();
        await Factory.DisposeAsync();
        try { Directory.Delete(EmbeddingsDir, recursive: true); } catch { /* test cleanup */ }
    }
}

internal sealed class InMemoryDbContextFactory : IDbContextFactory<VoxMindSpeakerDbContext>, IAsyncDisposable
{
    private readonly string _dbName = "voxmind-speakers-" + Guid.NewGuid().ToString("N");

    public VoxMindSpeakerDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<VoxMindSpeakerDbContext>()
            .UseInMemoryDatabase(_dbName)
            .Options;
        return new VoxMindSpeakerDbContext(options);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class StubExtractor : ISpeakerEmbeddingExtractor
{
    private readonly float[]? _vector;
    public bool IsLoaded { get; }

    public StubExtractor(bool loaded, float[]? vector = null)
    {
        IsLoaded = loaded;
        _vector = vector;
    }

    public static StubExtractor WithVector(float[] vector) => new(loaded: true, vector);

    public float[]? ExtractFromSamples(float[] samples) => _vector;
}

/// <summary>Returns each pre-canned vector once, in order; falls back to <c>null</c> after exhaustion.</summary>
internal sealed class SequencedExtractor : ISpeakerEmbeddingExtractor
{
    private readonly Queue<float[]> _vectors;

    public SequencedExtractor(params float[][] vectors)
    {
        _vectors = new Queue<float[]>(vectors);
    }

    public bool IsLoaded => true;

    public float[]? ExtractFromSamples(float[] samples)
        => _vectors.Count == 0 ? null : _vectors.Dequeue();
}

internal sealed class DummyMeterFactory : IMeterFactory
{
    public Meter Create(MeterOptions options) => new(options);
    public void Dispose() { }
}
