using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Seren.Application.Abstractions;
using Seren.Application.Characters.Personas;
using Seren.Domain.Entities;
using Seren.Infrastructure.OpenClaw;

namespace Seren.Infrastructure.Characters;

/// <summary>
/// Filesystem-backed <see cref="IPersonaWorkspaceWriter"/>. Writes
/// <c>IDENTITY.md</c> and <c>SOUL.md</c> atomically (tmp + rename) into
/// the OpenClaw workspace directory configured via
/// <see cref="OpenClawOptions.WorkspacePath"/>.
/// </summary>
/// <remarks>
/// Concurrency : a single <see cref="SemaphoreSlim"/> serialises all
/// writes so two near-simultaneous activations (rare, but possible with
/// multi-tab) never interleave <c>.tmp</c> → rename operations. Path
/// safety : we never concatenate user-supplied strings into the target
/// paths — only the two hard-coded filenames <c>IDENTITY.md</c> and
/// <c>SOUL.md</c> can be written. The configured workspace path is
/// resolved via <see cref="Path.GetFullPath(string)"/> once and re-used.
/// </remarks>
public sealed class FileSystemPersonaWorkspaceWriter : IPersonaWorkspaceWriter, IDisposable
{
    private const string IdentityFilename = "IDENTITY.md";
    private const string SoulFilename = "SOUL.md";

    private readonly string? _absoluteWorkspacePath;
    private readonly IPersonaWriterMetrics _metrics;
    private readonly ILogger<FileSystemPersonaWorkspaceWriter> _logger;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public FileSystemPersonaWorkspaceWriter(
        IOptions<OpenClawOptions> options,
        IPersonaWriterMetrics metrics,
        ILogger<FileSystemPersonaWorkspaceWriter> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _metrics = metrics;
        _logger = logger;

        var rawPath = options.Value.WorkspacePath;
        _absoluteWorkspacePath = string.IsNullOrWhiteSpace(rawPath)
            ? null
            : Path.GetFullPath(rawPath);
    }

    public async Task WritePersonaAsync(Character character, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(character);

        if (_absoluteWorkspacePath is null)
        {
            _metrics.RecordWrite("no_workspace", character.Name, bytesWritten: 0, TimeSpan.Zero);
            _logger.LogDebug(
                "Persona write skipped: no OpenClaw:WorkspacePath configured (character={Character})",
                character.Name);
            return;
        }

        var stopwatch = Stopwatch.StartNew();

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_absoluteWorkspacePath);

            var identityBytes = Encoding.UTF8.GetBytes(PersonaTemplateComposer.ComposeIdentity(character));
            var soulBytes = Encoding.UTF8.GetBytes(PersonaTemplateComposer.ComposeSoul(character));

            await WriteAtomicAsync(IdentityFilename, identityBytes, cancellationToken).ConfigureAwait(false);
            await WriteAtomicAsync(SoulFilename, soulBytes, cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();
            var total = (long)identityBytes.Length + soulBytes.Length;
            _metrics.RecordWrite("ok", character.Name, total, stopwatch.Elapsed);
            _logger.LogInformation(
                "Persona workspace refreshed for character {Character} ({Bytes} bytes, {ElapsedMs} ms)",
                character.Name, total, stopwatch.Elapsed.TotalMilliseconds);
        }
#pragma warning disable CA1031 // Best-effort: activation flow never fails due to persona-write errors.
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metrics.RecordWrite("error", character.Name, bytesWritten: 0, stopwatch.Elapsed);
            _logger.LogWarning(ex,
                "Persona workspace write failed for character {Character} at {Path} — activation continues.",
                character.Name, _absoluteWorkspacePath);
        }
#pragma warning restore CA1031
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>
    /// Write <paramref name="bytes"/> to
    /// <c>{workspace}/{filename}</c> atomically via a <c>.tmp</c>
    /// sibling + <see cref="File.Move(string, string, bool)"/>. Readers
    /// (OpenClaw agent runtime) can never observe a half-written file.
    /// </summary>
    private async Task WriteAtomicAsync(string filename, byte[] bytes, CancellationToken cancellationToken)
    {
        var finalPath = Path.Combine(_absoluteWorkspacePath!, filename);
        var tmpPath = finalPath + ".tmp";

        await using (var stream = File.Create(tmpPath))
        {
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(tmpPath, finalPath, overwrite: true);
    }

    public void Dispose() => _writeGate.Dispose();
}
