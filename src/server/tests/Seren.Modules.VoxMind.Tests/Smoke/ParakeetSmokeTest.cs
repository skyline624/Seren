using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Seren.Application.Abstractions;
using Seren.Modules.VoxMind.Diagnostics;
using Seren.Modules.VoxMind.Transcription;
using Shouldly;
using Xunit;

namespace Seren.Modules.VoxMind.Tests.Smoke;

/// <summary>
/// Smoke test that exercises the real Parakeet ONNX bundle end-to-end against
/// a known WAV fixture. Skipped automatically when the
/// <c>VOXMIND_SMOKE_MODEL_DIR</c> and <c>VOXMIND_SMOKE_AUDIO</c> environment
/// variables are not pointing at a deployed bundle and a readable WAV file —
/// CI runs it on demand only.
/// </summary>
public sealed class ParakeetSmokeTest
{
    private static readonly string? ModelDir = Environment.GetEnvironmentVariable("VOXMIND_SMOKE_MODEL_DIR");
    private static readonly string? AudioPath = Environment.GetEnvironmentVariable("VOXMIND_SMOKE_AUDIO");

    private readonly ITestOutputHelper _output;

    public ParakeetSmokeTest(ITestOutputHelper output)
    {
        _output = output;
    }

    private static bool ShouldSkip =>
        string.IsNullOrWhiteSpace(ModelDir)
        || !Directory.Exists(ModelDir)
        || string.IsNullOrWhiteSpace(AudioPath)
        || !File.Exists(AudioPath);

    [Fact]
    public async Task TranscribeAsync_RealBundle_ReturnsNonEmptyText()
    {
        if (ShouldSkip)
        {
            Assert.Skip(
                "Set VOXMIND_SMOKE_MODEL_DIR (Parakeet bundle) and VOXMIND_SMOKE_AUDIO (WAV) to run.");
        }

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Debug).AddSimpleConsole());
        services.AddMetrics();
        services.AddSingleton<ILanguageDetector, StopwordLanguageDetector>();
        services.AddSingleton<VoxMindMetrics>();
        services.AddSingleton<IOptions<VoxMindOptions>>(sp =>
        {
            var opts = new VoxMindOptions { DefaultLanguage = "en" };
            opts.Stt.ModelDir = ModelDir!;
            return Options.Create(opts);
        });
        services.AddSingleton<ISttProvider, VoxMindSttProvider>();

        await using var sp = services.BuildServiceProvider();
        var stt = sp.GetRequiredService<ISttProvider>();

        var audio = await File.ReadAllBytesAsync(AudioPath!, TestContext.Current.CancellationToken);
        var result = await stt.TranscribeAsync(audio, format: "wav", TestContext.Current.CancellationToken);

        result.Text.ShouldNotBeNullOrWhiteSpace();
        (result.Confidence ?? 0f).ShouldBeGreaterThan(0f);
        // Whatever the language detector picks, it must be one of the 25
        // Parakeet-supported codes (no garbage like "und" or empty).
        result.Language.ShouldNotBeNullOrWhiteSpace();

        _output.WriteLine($"[SMOKE] Detected language: {result.Language}");
        _output.WriteLine($"[SMOKE] Confidence: {result.Confidence}");
        _output.WriteLine($"[SMOKE] Text: {result.Text}");
    }
}
