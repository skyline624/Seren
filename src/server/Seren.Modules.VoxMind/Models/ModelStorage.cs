using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Seren.Modules.VoxMind.Models;

/// <summary>
/// Filesystem operations for STT bundles: presence detection and atomic
/// deletion. Kept separate from the download service so the routing layer
/// (and tests) can inspect on-disk state without owning a network stack.
/// </summary>
public interface IModelStorage
{
    /// <summary>
    /// Returns <c>true</c> when every file declared by the variant exists
    /// inside its install directory with a non-zero size. System-managed
    /// variants (Parakeet) defer to <see cref="VoxMindOptions.Stt"/> —
    /// presence is "directory exists and is non-empty".
    /// </summary>
    bool IsDownloaded(ModelVariant variant);

    /// <summary>
    /// Removes the variant's bundle directory atomically (rename then
    /// recursive delete). System-managed variants throw — the UI must not
    /// expose a delete affordance for them.
    /// </summary>
    /// <returns><c>true</c> if files were actually deleted; <c>false</c> when nothing was on disk.</returns>
    Task<bool> DeleteAsync(ModelVariant variant, CancellationToken ct = default);

    /// <summary>Resolves the variant's install directory (may be empty when not configured).</summary>
    string GetLocalDir(ModelVariant variant);
}

/// <inheritdoc />
public sealed class ModelStorage : IModelStorage
{
    private readonly VoxMindOptions _options;
    private readonly ILogger<ModelStorage> _logger;

    public ModelStorage(IOptions<VoxMindOptions> options, ILogger<ModelStorage> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsDownloaded(ModelVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);

        var dir = ModelCatalog.LocalDirFor(_options, variant);
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            return false;
        }

        // System-managed variants don't ship file manifests — the
        // deployment ops are responsible for the bundle layout. We
        // accept "directory exists with at least one regular file" as
        // a "downloaded" signal so Parakeet shows up correctly.
        if (variant.IsSystemManaged || variant.Files.Count == 0)
        {
            try
            {
                return Directory.EnumerateFiles(dir).Any();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "ModelStorage: failed to enumerate {Dir}.", dir);
                return false;
            }
        }

        for (var i = 0; i < variant.Files.Count; i++)
        {
            var path = Path.Combine(dir, variant.Files[i]);
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length == 0)
                {
                    return false;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "ModelStorage: failed to stat {Path}.", path);
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(ModelVariant variant, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(variant);

        if (variant.IsSystemManaged)
        {
            throw new InvalidOperationException(
                $"Refusing to delete system-managed variant '{variant.Id}'.");
        }

        var dir = ModelCatalog.LocalDirFor(_options, variant);
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            return false;
        }

        // Atomic delete via rename: anyone holding a stale handle (an
        // OfflineRecognizer mid-load) keeps reading the moved-aside copy
        // while readers checking `IsDownloaded` immediately see the slot
        // as free. Fallback to direct delete on failed rename.
        var trash = $"{dir}.deleting-{Guid.NewGuid():N}";
        try
        {
            Directory.Move(dir, trash);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex,
                "ModelStorage: rename-then-delete fell back to in-place delete for {Dir}.", dir);
            trash = dir;
        }

        try
        {
            await Task.Run(() => Directory.Delete(trash, recursive: true), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex,
                "ModelStorage: deletion of {Trash} failed — orphan directory left on disk.", trash);
            throw;
        }

        return true;
    }

    /// <inheritdoc />
    public string GetLocalDir(ModelVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        return ModelCatalog.LocalDirFor(_options, variant);
    }
}
