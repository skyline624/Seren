namespace Seren.Application.Abstractions;

/// <summary>
/// Persistence for 2D character avatars (PNG bytes) extracted from
/// imported Character Card v3 files. Kept intentionally narrow per
/// ISP — three verbs, no listing, no metadata querying — so it stays
/// easy to re-implement against any blob store (filesystem today,
/// S3/Azure Blob later) without touching consumers.
/// </summary>
/// <remarks>
/// The store is keyed by <see cref="Guid"/> (the character id). All
/// implementations must:
/// <list type="bullet">
/// <item><description><see cref="SaveAsync"/> — idempotent : saving a second
/// PNG under the same id overwrites the previous one atomically (readers
/// see either the old bytes or the new, never a truncated file).</description></item>
/// <item><description><see cref="OpenReadAsync"/> — returns <c>null</c> (not
/// an exception) when the id has no stored avatar, so the HTTP layer can
/// map "missing" to 404 without catching.</description></item>
/// <item><description><see cref="DeleteAsync"/> — idempotent no-op when the
/// id has no stored avatar. Callers (notably <c>DeleteCharacterHandler</c>)
/// can call it unconditionally after removing the character record.</description></item>
/// </list>
/// </remarks>
public interface ICharacterAvatarStore
{
    /// <summary>
    /// Persist <paramref name="pngBytes"/> as the avatar for <paramref name="characterId"/>.
    /// Returns the path the store associates with this avatar (relative
    /// to its configured root) so the caller can stamp
    /// <c>Character.AvatarImagePath</c> without knowing the storage layout.
    /// </summary>
    Task<string> SaveAsync(Guid characterId, byte[] pngBytes, CancellationToken cancellationToken);

    /// <summary>
    /// Open a stream over the stored PNG or return <c>null</c> if none
    /// exists. Caller disposes the stream.
    /// </summary>
    Task<Stream?> OpenReadAsync(Guid characterId, CancellationToken cancellationToken);

    /// <summary>
    /// Remove the stored avatar for <paramref name="characterId"/>.
    /// Idempotent no-op when absent.
    /// </summary>
    Task DeleteAsync(Guid characterId, CancellationToken cancellationToken);
}
