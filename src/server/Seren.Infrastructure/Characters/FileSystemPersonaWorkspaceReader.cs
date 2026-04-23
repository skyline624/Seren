using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Seren.Application.Abstractions;
using Seren.Infrastructure.OpenClaw;

namespace Seren.Infrastructure.Characters;

/// <summary>
/// Filesystem-backed <see cref="IPersonaWorkspaceReader"/>. Reads the
/// two persona files out of the OpenClaw workspace directory
/// configured by <see cref="OpenClawOptions.WorkspacePath"/>.
/// </summary>
/// <remarks>
/// Concurrency safety : the writer uses atomic <c>.tmp + rename</c>, so
/// this reader always observes either a fully-previous or a
/// fully-new file — never a half-written mix. No lock required. Path
/// resolution happens once at construction (same as the writer) so
/// every read shares a stable, pre-validated absolute path.
/// </remarks>
public sealed class FileSystemPersonaWorkspaceReader : IPersonaWorkspaceReader
{
    private const string IdentityFilename = "IDENTITY.md";
    private const string SoulFilename = "SOUL.md";

    private readonly string? _absoluteWorkspacePath;
    private readonly ILogger<FileSystemPersonaWorkspaceReader> _logger;

    public FileSystemPersonaWorkspaceReader(
        IOptions<OpenClawOptions> options,
        ILogger<FileSystemPersonaWorkspaceReader> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger;

        var rawPath = options.Value.WorkspacePath;
        _absoluteWorkspacePath = string.IsNullOrWhiteSpace(rawPath)
            ? null
            : Path.GetFullPath(rawPath);
    }

    public async Task<PersonaReadResult> ReadCurrentPersonaAsync(CancellationToken cancellationToken)
    {
        if (_absoluteWorkspacePath is null)
        {
            _logger.LogDebug("Persona read skipped: no OpenClaw:WorkspacePath configured.");
            return PersonaReadResult.NotConfigured;
        }

        var identityPath = Path.Combine(_absoluteWorkspacePath, IdentityFilename);
        var soulPath = Path.Combine(_absoluteWorkspacePath, SoulFilename);

        if (!File.Exists(identityPath) || !File.Exists(soulPath))
        {
            _logger.LogDebug(
                "Persona read reports empty workspace (identity={IdentityExists}, soul={SoulExists}, path={Path}).",
                File.Exists(identityPath), File.Exists(soulPath), _absoluteWorkspacePath);
            return PersonaReadResult.Empty;
        }

        var identity = await File.ReadAllTextAsync(identityPath, cancellationToken).ConfigureAwait(false);
        var soul = await File.ReadAllTextAsync(soulPath, cancellationToken).ConfigureAwait(false);

        return PersonaReadResult.Loaded(new WorkspacePersonaSnapshot(identity, soul));
    }
}
