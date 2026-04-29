using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Seren.Application.Abstractions;
using Seren.Modules.VoxMind;
using Seren.Modules.VoxMind.Transcription.Engines;
using Shouldly;
using Xunit;

namespace Seren.Modules.VoxMind.Tests.Smoke;

/// <summary>
/// Smoke test that exercises the real Whisper sherpa-onnx bundle end-to-end.
/// Skipped automatically when the <c>VOXMIND_SMOKE_WHISPER_DIR</c> and
/// <c>VOXMIND_SMOKE_AUDIO</c> environment variables are not pointing at a
/// deployed bundle and a readable WAV file — CI runs it on demand only.
/// </summary>
public sealed class WhisperSmokeTest
{
    private static readonly string? ModelDir = Environment.GetEnvironmentVariable("VOXMIND_SMOKE_WHISPER_DIR");
    private static readonly string? AudioPath = Environment.GetEnvironmentVariable("VOXMIND_SMOKE_AUDIO");
    private static readonly string ModelSize = Environment.GetEnvironmentVariable("VOXMIND_SMOKE_WHISPER_SIZE") ?? "small";

    private static bool ShouldSkip =>
        string.IsNullOrWhiteSpace(ModelDir)
        || !Directory.Exists(ModelDir)
        || string.IsNullOrWhiteSpace(AudioPath)
        || !File.Exists(AudioPath);

    private readonly ITestOutputHelper _output;

    public WhisperSmokeTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task TranscribeAsync_RealWhisperBundle_ReturnsNonEmptyText()
    {
        if (ShouldSkip)
        {
            Assert.Skip(
                "Set VOXMIND_SMOKE_WHISPER_DIR (sherpa-onnx Whisper bundle), "
                + "VOXMIND_SMOKE_AUDIO (WAV) and optional VOXMIND_SMOKE_WHISPER_SIZE to run.");
        }

        var opts = new VoxMindOptions { DefaultLanguage = "fr" };
        opts.Stt.Whisper.ModelDir = ModelDir!;
        opts.Stt.Whisper.ModelSize = ModelSize;

        using var loggerFactory = LoggerFactory.Create(b => b
            .SetMinimumLevel(LogLevel.Debug)
            .AddSimpleConsole());

        var engine = new WhisperSttEngine(
            Options.Create(opts),
            loggerFactory.CreateLogger<WhisperSttEngine>());

        var audio = await File.ReadAllBytesAsync(AudioPath!, TestContext.Current.CancellationToken);
        var result = await engine.TranscribeAsync(audio, format: "wav", TestContext.Current.CancellationToken);

        result.Text.ShouldNotBeNullOrWhiteSpace();
        (result.Confidence ?? 0f).ShouldBeGreaterThan(0f);
        result.Language.ShouldNotBeNullOrWhiteSpace();

        _output.WriteLine($"[SMOKE] Detected language: {result.Language}");
        _output.WriteLine($"[SMOKE] Confidence: {result.Confidence}");
        _output.WriteLine($"[SMOKE] Text: {result.Text}");
    }
}
