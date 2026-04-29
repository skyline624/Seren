using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Seren.Application.Abstractions;
using Seren.Modules.VoxMind;
using Seren.Modules.VoxMind.Diagnostics;
using Seren.Modules.VoxMind.Transcription;
using Shouldly;
using Xunit;

namespace Seren.Modules.VoxMind.Tests.Transcription;

/// <summary>
/// Unit tests for the per-request engine selection logic in
/// <see cref="VoxMindSttProvider"/>. The router is a small dispatcher;
/// engines are mocked so we never touch ONNX runtime.
/// </summary>
public sealed class VoxMindSttProviderRoutingTests : IDisposable
{
    private readonly IVoxMindSttEngine _parakeet;
    private readonly IVoxMindSttEngine _whisper;
    private readonly VoxMindMetrics _metrics;
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _metrics.Dispose();
    }

    public VoxMindSttProviderRoutingTests()
    {
        _parakeet = Substitute.For<IVoxMindSttEngine>();
        _parakeet.Name.Returns("parakeet");
        _parakeet.IsAvailable.Returns(true);
        _parakeet.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new SttResult("from-parakeet", "fr", 0.9f)));

        _whisper = Substitute.For<IVoxMindSttEngine>();
        _whisper.Name.Returns("whisper");
        _whisper.IsAvailable.Returns(true);
        _whisper.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new SttResult("from-whisper", "fr", 0.95f)));

        _metrics = new VoxMindMetrics(new TestMeterFactory());
    }

    [Fact]
    public async Task EngineHint_Whisper_RoutesToWhisper()
    {
        var router = BuildRouter(defaultEngine: "parakeet");

        var result = await router.TranscribeAsync(
            [1, 2, 3], "wav", engineHint: "whisper", TestContext.Current.CancellationToken);

        result.Text.ShouldBe("from-whisper");
        await _whisper.Received(1).TranscribeAsync(Arg.Any<byte[]>(), "wav", Arg.Any<CancellationToken>());
        await _parakeet.DidNotReceive().TranscribeAsync(
            Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EngineHint_Parakeet_RoutesToParakeet()
    {
        var router = BuildRouter(defaultEngine: "whisper");

        var result = await router.TranscribeAsync(
            [1, 2, 3], "wav", engineHint: "parakeet", TestContext.Current.CancellationToken);

        result.Text.ShouldBe("from-parakeet");
    }

    [Fact]
    public async Task EngineHint_Null_UsesConfiguredDefault()
    {
        var router = BuildRouter(defaultEngine: "whisper");

        var result = await router.TranscribeAsync(
            [1, 2, 3], "wav", engineHint: null, TestContext.Current.CancellationToken);

        result.Text.ShouldBe("from-whisper");
    }

    [Fact]
    public async Task EngineHint_Unknown_FallsBackToDefault()
    {
        var router = BuildRouter(defaultEngine: "parakeet");

        var result = await router.TranscribeAsync(
            [1, 2, 3], "wav", engineHint: "babbage-3000", TestContext.Current.CancellationToken);

        result.Text.ShouldBe("from-parakeet");
    }

    [Fact]
    public async Task EngineHint_Whisper_UnavailableFallsBackToDefault()
    {
        _whisper.IsAvailable.Returns(false);
        var router = BuildRouter(defaultEngine: "parakeet");

        var result = await router.TranscribeAsync(
            [1, 2, 3], "wav", engineHint: "whisper", TestContext.Current.CancellationToken);

        result.Text.ShouldBe("from-parakeet");
    }

    [Fact]
    public async Task NoEngineAvailable_ReturnsEmptyResult()
    {
        _parakeet.IsAvailable.Returns(false);
        _whisper.IsAvailable.Returns(false);
        var router = BuildRouter(defaultEngine: "parakeet");

        var result = await router.TranscribeAsync(
            [1, 2, 3], "wav", engineHint: null, TestContext.Current.CancellationToken);

        result.Text.ShouldBe(string.Empty);
        result.Language.ShouldBe("fr");
    }

    [Fact]
    public async Task LegacyOverloadWithoutHint_UsesDefault()
    {
        var router = BuildRouter(defaultEngine: "whisper");

        var result = await router.TranscribeAsync(
            [1, 2, 3], "wav", TestContext.Current.CancellationToken);

        result.Text.ShouldBe("from-whisper");
    }

    private VoxMindSttProvider BuildRouter(string defaultEngine)
    {
        var opts = new VoxMindOptions { DefaultLanguage = "fr" };
        opts.Stt.DefaultEngine = defaultEngine;
        var snapshot = Options.Create(opts);
        return new VoxMindSttProvider(
            snapshot,
            NullLogger<VoxMindSttProvider>.Instance,
            _metrics,
            new[] { _parakeet, _whisper });
    }

    /// <summary>
    /// Bare-bones <see cref="System.Diagnostics.Metrics.IMeterFactory"/>
    /// for unit tests — the router only needs the metrics surface to
    /// record counters; the meter itself is irrelevant.
    /// </summary>
    private sealed class TestMeterFactory : System.Diagnostics.Metrics.IMeterFactory
    {
        public System.Diagnostics.Metrics.Meter Create(System.Diagnostics.Metrics.MeterOptions options)
            => new(options);

        public void Dispose() { }
    }
}
