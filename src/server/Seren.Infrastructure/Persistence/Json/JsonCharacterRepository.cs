using System.Text.Json;
using Microsoft.Extensions.Options;
using Seren.Application.Abstractions;
using Seren.Domain.Entities;

namespace Seren.Infrastructure.Persistence.Json;

/// <summary>
/// Flat-file implementation of <see cref="ICharacterRepository"/>. The
/// entire character list is serialised as JSON to <see cref="CharacterStoreOptions.StorePath"/>,
/// loaded lazily on first access, and written atomically via a
/// temp-file + <see cref="File.Move(string, string, bool)"/>.
/// </summary>
/// <remarks>
/// <para>
/// Scale target: &lt; 1000 characters per installation. For anything
/// larger, swap back to EF Core or introduce a real database — but
/// for personal / small-team Seren deployments the complexity of EF +
/// SQLite is not worth it (avatars + voice + agent id = 9 fields).
/// </para>
/// <para>
/// Concurrency: a single <see cref="SemaphoreSlim"/> serialises every
/// read/write, making the repository safe for multi-request ASP.NET
/// Core use. Writes are atomic against process crashes because
/// <see cref="File.Move(string, string, bool)"/> with <c>overwrite: true</c>
/// is atomic on both NTFS and POSIX filesystems.
/// </para>
/// <para>
/// Registered as a <see cref="ServiceLifetime.Singleton"/> in DI so the
/// in-memory cache is shared across requests.
/// </para>
/// </remarks>
public sealed class JsonCharacterRepository : ICharacterRepository, IDisposable
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<Character>? _cache;

    public JsonCharacterRepository(IOptions<CharacterStoreOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _path = options.Value.StorePath;
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    /// <inheritdoc />
    public async Task<Character?> GetActiveAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            return _cache!.FirstOrDefault(c => c.IsActive);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<Character?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            return _cache!.FirstOrDefault(c => c.Id == id);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Character>> GetAllAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            return [.. _cache!.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)];
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task AddAsync(Character character, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(character);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            _cache!.Add(character);
            await PersistAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task UpdateAsync(Character character, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(character);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var index = _cache!.FindIndex(c => c.Id == character.Id);
            if (index < 0)
            {
                throw new InvalidOperationException($"Character '{character.Id}' not found.");
            }

            _cache[index] = character;
            await PersistAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var removed = _cache!.RemoveAll(c => c.Id == id);
            if (removed > 0)
            {
                await PersistAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SetActiveAsync(Guid id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var index = _cache!.FindIndex(c => c.Id == id);
            if (index < 0)
            {
                throw new InvalidOperationException($"Character '{id}' not found.");
            }

            // Atomic flip: rebuild the list so only the target is active,
            // preserving all other mutable state and updating UpdatedAt on
            // rows whose IsActive flag actually changed.
            var now = DateTimeOffset.UtcNow;
            for (var i = 0; i < _cache.Count; i++)
            {
                var c = _cache[i];
                var shouldBeActive = c.Id == id;
                if (c.IsActive != shouldBeActive)
                {
                    _cache[i] = c with { IsActive = shouldBeActive, UpdatedAt = now };
                }
            }

            await PersistAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    // ── Internal ────────────────────────────────────────────────────────

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_cache is not null)
        {
            return;
        }

        if (!File.Exists(_path))
        {
            _cache = [];
            return;
        }

        await using var stream = File.OpenRead(_path);
        if (stream.Length == 0)
        {
            _cache = [];
            return;
        }

        var list = await JsonSerializer.DeserializeAsync(
            stream,
            CharacterJsonContext.Default.ListCharacter,
            cancellationToken).ConfigureAwait(false);
        _cache = list ?? [];
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Write to a temp file then atomically rename so readers never see
        // a half-written JSON document.
        var tmp = _path + ".tmp";
        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                _cache!,
                CharacterJsonContext.Default.ListCharacter,
                cancellationToken).ConfigureAwait(false);
        }

        File.Move(tmp, _path, overwrite: true);
    }
}
