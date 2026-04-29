using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Seren.Modules.VoxMind.Diagnostics;
using Seren.Modules.VoxMind.F5Tts;
using Shouldly;
using Xunit;

namespace Seren.Modules.VoxMind.Tests.Diagnostics;

public sealed class VoxMindHealthCheckTests
{
    [Fact]
    public async Task Parakeet_DegradedWhenModelDirEmpty()
    {
        var check = new ParakeetHealthCheck(Options.Create(new VoxMindOptions()));

        var result = await check.CheckHealthAsync(new HealthCheckContext(),
            TestContext.Current.CancellationToken);

        result.Status.ShouldBe(HealthStatus.Degraded);
    }

    [Fact]
    public async Task Parakeet_UnhealthyWhenDirSetButMissing()
    {
        var opts = new VoxMindOptions();
        opts.Stt.Parakeet.ModelDir = Path.Combine(Path.GetTempPath(), "voxmind-missing-" + Guid.NewGuid());
        var check = new ParakeetHealthCheck(Options.Create(opts));

        var result = await check.CheckHealthAsync(new HealthCheckContext(),
            TestContext.Current.CancellationToken);

        result.Status.ShouldBe(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task Parakeet_UnhealthyWhenDirExistsButMissingFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "voxmind-empty-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var opts = new VoxMindOptions();
            opts.Stt.Parakeet.ModelDir = dir;
            var check = new ParakeetHealthCheck(Options.Create(opts));

            var result = await check.CheckHealthAsync(new HealthCheckContext(),
                TestContext.Current.CancellationToken);

            result.Status.ShouldBe(HealthStatus.Unhealthy);
            result.Description!.ShouldContain("missing");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Parakeet_HealthyWhenAllFilesPresent()
    {
        var dir = Path.Combine(Path.GetTempPath(), "voxmind-full-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var name in new[] { "nemo128.onnx", "encoder-model.int8.onnx",
                "decoder_joint-model.int8.onnx", "vocab.txt" })
            {
                await File.WriteAllTextAsync(Path.Combine(dir, name), "stub",
                    TestContext.Current.CancellationToken);
            }
            var opts = new VoxMindOptions();
            opts.Stt.Parakeet.ModelDir = dir;
            var check = new ParakeetHealthCheck(Options.Create(opts));

            var result = await check.CheckHealthAsync(new HealthCheckContext(),
                TestContext.Current.CancellationToken);

            result.Status.ShouldBe(HealthStatus.Healthy);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Parakeet_HealthyWhenModuleDisabled()
    {
        var opts = new VoxMindOptions { Enabled = false };
        opts.Stt.Parakeet.ModelDir = "/this/does/not/exist";
        var check = new ParakeetHealthCheck(Options.Create(opts));

        var result = await check.CheckHealthAsync(new HealthCheckContext(),
            TestContext.Current.CancellationToken);

        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Whisper_DegradedWhenModelDirEmpty()
    {
        var check = new WhisperHealthCheck(Options.Create(new VoxMindOptions()));

        var result = await check.CheckHealthAsync(new HealthCheckContext(),
            TestContext.Current.CancellationToken);

        result.Status.ShouldBe(HealthStatus.Degraded);
    }

    [Fact]
    public async Task Whisper_UnhealthyWhenDirExistsButMissingFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "voxmind-whisper-empty-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var opts = new VoxMindOptions();
            opts.Stt.Whisper.ModelDir = dir;
            opts.Stt.Whisper.ModelSize = "small";
            var check = new WhisperHealthCheck(Options.Create(opts));

            var result = await check.CheckHealthAsync(new HealthCheckContext(),
                TestContext.Current.CancellationToken);

            result.Status.ShouldBe(HealthStatus.Unhealthy);
            result.Description!.ShouldContain("missing");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Whisper_HealthyWhenAllFilesPresent()
    {
        var dir = Path.Combine(Path.GetTempPath(), "voxmind-whisper-full-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var name in new[]
            {
                "small-encoder.int8.onnx",
                "small-decoder.int8.onnx",
                "small-tokens.txt",
            })
            {
                await File.WriteAllTextAsync(Path.Combine(dir, name), "stub",
                    TestContext.Current.CancellationToken);
            }
            var opts = new VoxMindOptions();
            opts.Stt.Whisper.ModelDir = dir;
            opts.Stt.Whisper.ModelSize = "small";
            var check = new WhisperHealthCheck(Options.Create(opts));

            var result = await check.CheckHealthAsync(new HealthCheckContext(),
                TestContext.Current.CancellationToken);

            result.Status.ShouldBe(HealthStatus.Healthy);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task F5Tts_DegradedWhenNoLanguageDeclared()
    {
        var check = new F5TtsHealthCheck(Options.Create(new VoxMindOptions()));

        var result = await check.CheckHealthAsync(new HealthCheckContext(),
            TestContext.Current.CancellationToken);

        result.Status.ShouldBe(HealthStatus.Degraded);
    }

    [Fact]
    public async Task F5Tts_DegradedWhenDeclaredButFilesMissing()
    {
        var opts = new VoxMindOptions();
        opts.Tts.Languages["fr"] = new F5LanguageCheckpoint
        {
            Language = "fr",
            PreprocessModelPath = "/no/such/file.onnx",
            TransformerModelPath = "/no/such/file.onnx",
            DecodeModelPath = "/no/such/file.onnx",
            TokensPath = "/no/such/file.txt",
        };
        var check = new F5TtsHealthCheck(Options.Create(opts));

        var result = await check.CheckHealthAsync(new HealthCheckContext(),
            TestContext.Current.CancellationToken);

        result.Status.ShouldBe(HealthStatus.Degraded);
    }
}
