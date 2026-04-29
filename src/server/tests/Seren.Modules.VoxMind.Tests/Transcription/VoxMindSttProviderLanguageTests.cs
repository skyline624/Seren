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
/// Routing tests covering the (engineHint, languageHint) pair on
/// <see cref="VoxMindSttProvider"/>. Whisper is mocked through both
/// <see cref="IVoxMindSttEngine"/> and
/// <see cref="IVoxMindVariantAwareEngine"/> so we can assert the router
/// forwards the size + language without dragging sherpa-onnx into the
/// test suite.
/// </summary>
public sealed class VoxMindSttProviderLanguageTests : IDisposable
{
    private readonly IVoxMindSttEngine _parakeet;
    private readonly VariantAwareEngineSubstitute _whisper;
    private readonly VoxMindMetrics _metrics;
    private bool _disposed;

    public VoxMindSttProviderLanguageTests()
    {
        _parakeet = Substitute.For<IVoxMindSttEngine>();
        _parakeet.Name.Returns("parakeet");
        _parakeet.IsAvailable.Returns(true);
        _parakeet.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new SttResult("from-parakeet", "fr", 0.9f)));

        _whisper = new VariantAwareEngineSubstitute();
        _metrics = new VoxMindMetrics(new TestMeterFactory());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _metrics.Dispose();
    }

    [Fact]
    public async Task EngineHint_WhisperWithSize_ForwardsSizeToVariantEngine()
    {
        var router = BuildRouter(defaultEngine: "parakeet");

        await router.TranscribeAsync(
            [1, 2, 3], "wav", engineHint: "whisper-tiny", languageHint: null,
            TestContext.Current.CancellationToken);

        _whisper.Calls.Count.ShouldBe(1);
        _whisper.Calls[0].Size.ShouldBe("tiny");
        _whisper.Calls[0].Language.ShouldBeNull();
    }

    [Fact]
    public async Task LanguageHint_Fr_ForwardsLowercaseToVariantEngine()
    {
        var router = BuildRouter(defaultEngine: "whisper");

        await router.TranscribeAsync(
            [1, 2, 3], "wav", engineHint: "whisper-small", languageHint: "FR",
            TestContext.Current.CancellationToken);

        _whisper.Calls.Single().Size.ShouldBe("small");
        _whisper.Calls.Single().Language.ShouldBe("fr");
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("AUTO")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task LanguageHint_AutoOrBlank_NormalisesToNull(string? hint)
    {
        var router = BuildRouter(defaultEngine: "whisper");

        await router.TranscribeAsync(
            [1, 2, 3], "wav", engineHint: "whisper-small", languageHint: hint,
            TestContext.Current.CancellationToken);

        _whisper.Calls.Single().Language.ShouldBeNull();
    }

    [Fact]
    public async Task EngineHint_BareWhisper_FallsBackToConfiguredSize()
    {
        var router = BuildRouter(defaultEngine: "whisper", configuredSize: "small");

        await router.TranscribeAsync(
            [1, 2, 3], "wav", engineHint: "whisper", languageHint: "en",
            TestContext.Current.CancellationToken);

        _whisper.Calls.Single().Size.ShouldBe("small");
        _whisper.Calls.Single().Language.ShouldBe("en");
    }

    [Fact]
    public async Task EngineHint_Parakeet_DoesNotInvokeVariantEngine()
    {
        var router = BuildRouter(defaultEngine: "whisper");

        var result = await router.TranscribeAsync(
            [1, 2, 3], "wav", engineHint: "parakeet", languageHint: "fr",
            TestContext.Current.CancellationToken);

        result.Text.ShouldBe("from-parakeet");
        _whisper.Calls.ShouldBeEmpty();
    }

    private VoxMindSttProvider BuildRouter(string defaultEngine, string configuredSize = "small")
    {
        var opts = new VoxMindOptions { DefaultLanguage = "fr" };
        opts.Stt.DefaultEngine = defaultEngine;
        opts.Stt.Whisper.ModelSize = configuredSize;
        var snapshot = Options.Create(opts);
        return new VoxMindSttProvider(
            snapshot,
            NullLogger<VoxMindSttProvider>.Instance,
            _metrics,
            new IVoxMindSttEngine[] { _parakeet, _whisper });
    }

    /// <summary>
    /// Hand-rolled fake that implements both surfaces — NSubstitute's
    /// runtime proxy can't be picked up by <c>is IVoxMindVariantAwareEngine</c>
    /// when the substitute is built off <c>IVoxMindSttEngine</c> alone,
    /// so we use a small concrete double instead.
    /// </summary>
    private sealed class VariantAwareEngineSubstitute : IVoxMindSttEngine, IVoxMindVariantAwareEngine
    {
        public List<(string Size, string? Language)> Calls { get; } = new();

        public string Name => "whisper";
        public bool IsAvailable => true;

        public Task<SttResult> TranscribeAsync(byte[] audioData, string format, CancellationToken ct = default)
        {
            // Should never be hit by the language-aware path — the router
            // dispatches via IVoxMindVariantAwareEngine instead.
            Calls.Add(("plain", null));
            return Task.FromResult(new SttResult("from-whisper-plain", "fr", 0.9f));
        }

        public Task<SttResult> TranscribeAsync(
            byte[] audioData, string format, string size, string? language, CancellationToken ct = default)
        {
            Calls.Add((size, language));
            return Task.FromResult(new SttResult("from-whisper-variant", language ?? "fr", 0.95f));
        }
    }

    private sealed class TestMeterFactory : System.Diagnostics.Metrics.IMeterFactory
    {
        public System.Diagnostics.Metrics.Meter Create(System.Diagnostics.Metrics.MeterOptions options)
            => new(options);

        public void Dispose() { }
    }
}
