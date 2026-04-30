using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Seren.Modules.VoxMind.Diagnostics;

/// <summary>
/// Health probe for the speaker-recognition subsystem. Reports
/// <c>Healthy</c> when the configured ONNX model + SQLite directory are
/// present on disk, <c>Degraded</c> when the operator has not deployed
/// the bundle yet (the rest of the app keeps working — the bubbles fall
/// back to the generic <c>You</c> label), and <c>Unhealthy</c> when the
/// configured paths are clearly broken (e.g. db parent directory does
/// not exist and cannot be created).
/// </summary>
public sealed class SpeakerIdHealthCheck : IHealthCheck
{
    public const string Name = "voxmind:speaker_identification";

    private readonly IOptions<VoxMindOptions> _options;

    public SpeakerIdHealthCheck(IOptions<VoxMindOptions> options)
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

        var speakers = opts.Speakers;
        if (!speakers.Enabled)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                "Speaker recognition disabled (Modules:voxmind:Speakers:Enabled=false)."));
        }

        if (string.IsNullOrWhiteSpace(speakers.ModelPath) || !File.Exists(speakers.ModelPath))
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"Speaker embedding model missing on disk: {speakers.ModelPath}. Deploy it under the voxmind_speakers volume to activate identification."));
        }

        var dbDir = Path.GetDirectoryName(speakers.DbPath);
        if (string.IsNullOrWhiteSpace(dbDir) || !Directory.Exists(dbDir))
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Speaker SQLite directory does not exist: {dbDir}. Mount the voxmind_speakers volume on this container."));
        }

        if (!string.IsNullOrWhiteSpace(speakers.EmbeddingsDir) && !Directory.Exists(speakers.EmbeddingsDir))
        {
            // The service creates the directory lazily, so a missing one
            // is recoverable — flag as Degraded rather than Unhealthy.
            return Task.FromResult(HealthCheckResult.Degraded(
                $"Speaker embeddings directory does not exist yet: {speakers.EmbeddingsDir}. It will be created on the first identification call."));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            $"Speaker recognition ready (model={Path.GetFileName(speakers.ModelPath)}, db={Path.GetFileName(speakers.DbPath)})."));
    }
}
