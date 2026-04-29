using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Seren.Modules.VoxMind.F5Tts;

namespace Seren.Modules.VoxMind.Diagnostics;

/// <summary>
/// Health probe for the Parakeet STT bundle. Checks the configured
/// <see cref="VoxMindSttOptions.ModelDir"/> exists on disk and contains the
/// four expected files. Reports <c>Degraded</c> when the operator has not
/// provided a model directory (the module no-ops gracefully) and
/// <c>Unhealthy</c> when the directory is configured but incomplete.
/// </summary>
public sealed class ParakeetHealthCheck : IHealthCheck
{
    /// <summary>Health-check name registered against <see cref="IHealthChecksBuilder"/>.</summary>
    public const string Name = "voxmind:parakeet";

    private static readonly string[] RequiredFiles =
    [
        "nemo128.onnx",
        "encoder-model.int8.onnx",
        "decoder_joint-model.int8.onnx",
        "vocab.txt",
    ];

    private readonly IOptions<VoxMindOptions> _options;

    public ParakeetHealthCheck(IOptions<VoxMindOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var opts = _options.Value;
        if (!opts.Enabled)
        {
            return Task.FromResult(HealthCheckResult.Healthy("VoxMind module disabled."));
        }

        var dir = opts.Stt.Parakeet.ModelDir;
        if (string.IsNullOrWhiteSpace(dir))
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "Modules:voxmind:Stt:Parakeet:ModelDir is empty — Parakeet engine inactive."));
        }

        if (!Directory.Exists(dir))
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Configured Modules:voxmind:Stt:Parakeet:ModelDir does not exist: {dir}."));
        }

        var missing = RequiredFiles
            .Where(f => !File.Exists(Path.Combine(dir, f)))
            .ToArray();
        if (missing.Length > 0)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Parakeet bundle in {dir} is missing required files: {string.Join(", ", missing)}."));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            $"Parakeet ONNX bundle present in {dir}."));
    }
}

/// <summary>
/// Health probe for the Whisper STT bundle (sherpa-onnx export). Mirror of
/// <see cref="ParakeetHealthCheck"/>: <c>Degraded</c> when ModelDir is
/// empty (engine simply unavailable), <c>Unhealthy</c> when it is set but
/// the encoder/decoder/tokens triplet is incomplete, <c>Healthy</c> when
/// all three files are on disk.
/// </summary>
public sealed class WhisperHealthCheck : IHealthCheck
{
    public const string Name = "voxmind:whisper";

    private readonly IOptions<VoxMindOptions> _options;

    public WhisperHealthCheck(IOptions<VoxMindOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var opts = _options.Value;
        if (!opts.Enabled)
        {
            return Task.FromResult(HealthCheckResult.Healthy("VoxMind module disabled."));
        }

        var dir = opts.Stt.Whisper.ModelDir;
        if (string.IsNullOrWhiteSpace(dir))
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "Modules:voxmind:Stt:Whisper:ModelDir is empty — Whisper engine inactive."));
        }

        if (!Directory.Exists(dir))
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Configured Modules:voxmind:Stt:Whisper:ModelDir does not exist: {dir}."));
        }

        var size = opts.Stt.Whisper.ModelSize;
        string[] required =
        [
            $"{size}-encoder.int8.onnx",
            $"{size}-decoder.int8.onnx",
            $"{size}-tokens.txt",
        ];

        var missing = required
            .Where(f => !File.Exists(Path.Combine(dir, f)))
            .ToArray();
        if (missing.Length > 0)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Whisper bundle in {dir} is missing required files: {string.Join(", ", missing)}."));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            $"Whisper ONNX bundle present in {dir} (size={size})."));
    }
}

/// <summary>
/// Health probe for the F5-TTS engines. Healthy when at least one declared
/// language has all four checkpoint files on disk; Degraded when none is
/// fully deployable (the TTS provider yields no chunks but the rest of the
/// app stays functional with the cloud fallback).
/// </summary>
public sealed class F5TtsHealthCheck : IHealthCheck
{
    public const string Name = "voxmind:f5tts";

    private readonly IOptions<VoxMindOptions> _options;

    public F5TtsHealthCheck(IOptions<VoxMindOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var opts = _options.Value;
        if (!opts.Enabled)
        {
            return Task.FromResult(HealthCheckResult.Healthy("VoxMind module disabled."));
        }

        if (opts.Tts.Languages.Count == 0)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "No F5-TTS language declared in Modules:voxmind:Tts:Languages — local TTS inactive."));
        }

        var ready = opts.Tts.Languages
            .Where(kv => CheckpointFullyOnDisk(kv.Value))
            .Select(kv => kv.Key)
            .ToArray();

        if (ready.Length == 0)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"None of the {opts.Tts.Languages.Count} declared F5 checkpoints are fully on disk."));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            $"F5-TTS checkpoints ready for: {string.Join(", ", ready)}."));
    }

    private static bool CheckpointFullyOnDisk(F5LanguageCheckpoint c)
        => !string.IsNullOrWhiteSpace(c.PreprocessModelPath)
        && File.Exists(c.PreprocessModelPath)
        && File.Exists(c.TransformerModelPath)
        && File.Exists(c.DecodeModelPath)
        && File.Exists(c.TokensPath);
}
