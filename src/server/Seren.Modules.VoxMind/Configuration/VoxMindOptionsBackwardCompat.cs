using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Seren.Modules.VoxMind.Configuration;

/// <summary>
/// Maps legacy single-engine STT config (<c>Modules:voxmind:Stt:ModelDir</c>)
/// onto the new per-engine path (<c>Modules:voxmind:Stt:Parakeet:ModelDir</c>)
/// so deployments that predate the multi-engine refactor keep working without
/// edits to <c>docker-compose.yml</c>.
/// </summary>
/// <remarks>
/// Runs as <see cref="IPostConfigureOptions{TOptions}"/> after binding so we
/// observe the user's settings exactly once at boot. Only fills the new
/// path when it's still empty — explicit per-engine config always wins.
/// Logs a one-line warning per migration event so operators know the
/// legacy field is on borrowed time.
/// </remarks>
public sealed class VoxMindOptionsBackwardCompat : IPostConfigureOptions<VoxMindOptions>
{
    private readonly ILogger<VoxMindOptionsBackwardCompat> _logger;

    public VoxMindOptionsBackwardCompat(ILogger<VoxMindOptionsBackwardCompat> logger)
    {
        _logger = logger;
    }

    public void PostConfigure(string? name, VoxMindOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!string.IsNullOrWhiteSpace(options.Stt.ModelDir)
            && string.IsNullOrWhiteSpace(options.Stt.Parakeet.ModelDir))
        {
            options.Stt.Parakeet.ModelDir = options.Stt.ModelDir;
            _logger.LogWarning(
                "Legacy Modules:voxmind:Stt:ModelDir detected ({Dir}). Mapping to Modules:voxmind:Stt:Parakeet:ModelDir. "
                + "Update your config to silence this warning.",
                options.Stt.ModelDir);
        }
    }
}
