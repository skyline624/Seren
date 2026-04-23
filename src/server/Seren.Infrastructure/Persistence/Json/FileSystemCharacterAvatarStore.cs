using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Seren.Application.Abstractions;

namespace Seren.Infrastructure.Persistence.Json;

/// <summary>
/// Filesystem-backed <see cref="ICharacterAvatarStore"/>. Stores one
/// PNG per character under <see cref="CharacterStoreOptions.AvatarDirectory"/>,
/// named <c>&lt;id&gt;.png</c>. Writes are atomic (<c>.tmp</c> + rename)
/// so readers never observe a half-written image; reads return the
/// stream and let the caller dispose it.
/// </summary>
/// <remarks>
/// Path handling: every operation resolves the target via
/// <see cref="Path.Combine(string, string)"/> with a single
/// <c>Guid.ToString("N")</c> segment — no caller-provided string ever
/// touches the path, so directory traversal is structurally impossible.
/// The avatar directory itself is validated by
/// <c>CharacterStoreOptionsValidator</c> at startup to ensure it stays
/// inside the configured store root.
/// </remarks>
public sealed class FileSystemCharacterAvatarStore : ICharacterAvatarStore
{
    private readonly string _directory;
    private readonly ILogger<FileSystemCharacterAvatarStore> _logger;

    public FileSystemCharacterAvatarStore(
        IOptions<CharacterStoreOptions> options,
        ILogger<FileSystemCharacterAvatarStore> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger;

        // Resolve AvatarDirectory relative to the StorePath's directory,
        // not the current working directory — this matches how the
        // characters.json file itself is resolved and keeps the default
        // `avatars/` sub-folder next to the characters file as intended.
        var value = options.Value;
        var storeDir = Path.GetDirectoryName(Path.GetFullPath(value.StorePath)) ?? Directory.GetCurrentDirectory();
        var candidate = Path.IsPathRooted(value.AvatarDirectory)
            ? value.AvatarDirectory
            : Path.Combine(storeDir, value.AvatarDirectory);
        _directory = Path.GetFullPath(candidate);
    }

    public async Task<string> SaveAsync(Guid characterId, byte[] pngBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);

        Directory.CreateDirectory(_directory);
        var fileName = $"{characterId:N}.png";
        var finalPath = Path.Combine(_directory, fileName);
        var tmpPath = finalPath + ".tmp";

        await using (var stream = File.Create(tmpPath))
        {
            await stream.WriteAsync(pngBytes, cancellationToken).ConfigureAwait(false);
        }

        // File.Move with overwrite is atomic on both NTFS and POSIX, so
        // concurrent readers either see the old file or the new one —
        // never a truncated mix.
        File.Move(tmpPath, finalPath, overwrite: true);

        _logger.LogDebug(
            "Saved avatar for character {CharacterId} ({Bytes} bytes) at {Path}",
            characterId, pngBytes.Length, finalPath);

        // Return a path relative to the directory so the Character record
        // doesn't bake an absolute host path into persistence.
        return Path.Combine("avatars", fileName);
    }

    public Task<Stream?> OpenReadAsync(Guid characterId, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_directory, $"{characterId:N}.png");
        if (!File.Exists(path))
        {
            return Task.FromResult<Stream?>(null);
        }
        Stream stream = File.OpenRead(path);
        return Task.FromResult<Stream?>(stream);
    }

    public Task DeleteAsync(Guid characterId, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_directory, $"{characterId:N}.png");
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                _logger.LogDebug("Deleted avatar for character {CharacterId}", characterId);
            }
        }
#pragma warning disable CA1031 // Idempotent cleanup: swallow IO failures, just log them.
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete avatar for character {CharacterId} at {Path}", characterId, path);
        }
#pragma warning restore CA1031

        return Task.CompletedTask;
    }
}
