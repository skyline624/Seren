using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Seren.Infrastructure.OpenClaw.Identity;

/// <summary>
/// Reads and (on first boot) creates Seren's Ed25519 device identity on disk.
/// The default location is <c>/data/seren-device-identity.json</c> — mounted
/// from the <c>seren_data</c> named volume in <c>docker-compose.yml</c>, so
/// the identity survives container rebuilds as long as the volume sticks.
/// </summary>
/// <remarks>
/// Once generated the file contains a 32-byte Ed25519 private seed — treat
/// it like a secret (Docker volume permissions + physical host access are
/// the same trust boundary as <c>characters.json</c> next to it).
/// </remarks>
public sealed class FileSystemDeviceIdentityStore : IDeviceIdentityStore, IDisposable
{
    private readonly string _path;
    private readonly ILogger<FileSystemDeviceIdentityStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DeviceIdentity? _cache;

    public FileSystemDeviceIdentityStore(
        IOptions<OpenClaw.OpenClawOptions> options,
        ILogger<FileSystemDeviceIdentityStore> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _path = options.Value.DeviceIdentityPath;
        _logger = logger;
    }

    // Test-friendly overload so unit tests can target a temp path without
    // materialising a full OpenClawOptions + IOptions wrapper.
    internal FileSystemDeviceIdentityStore(string path, ILogger<FileSystemDeviceIdentityStore> logger)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(logger);
        _path = path;
        _logger = logger;
    }

    public void Dispose() => _gate.Dispose();

    public async Task<DeviceIdentity> LoadOrCreateAsync(CancellationToken cancellationToken)
    {
        if (_cache is not null)
        {
            return _cache;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cache is not null)
            {
                return _cache;
            }

            _cache = File.Exists(_path)
                ? await LoadAsync(cancellationToken).ConfigureAwait(false)
                : await CreateAndPersistAsync(cancellationToken).ConfigureAwait(false);
            return _cache;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MarkPairedAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-load from disk to capture any concurrent edits, then layer
            // the paired marker on top.
            var current = _cache ?? (File.Exists(_path)
                ? await LoadAsync(cancellationToken).ConfigureAwait(false)
                : await CreateAndPersistAsync(cancellationToken).ConfigureAwait(false));

            if (current.PairedAtMs is not null)
            {
                _cache = current;
                return;
            }

            var updated = current with { PairedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
            await PersistAsync(updated, cancellationToken).ConfigureAwait(false);
            _cache = updated;
            _logger.LogInformation(
                "Device {DeviceId} paired with OpenClaw at {PairedAtMs}",
                updated.DeviceId, updated.PairedAtMs);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<DeviceIdentity> LoadAsync(CancellationToken cancellationToken)
    {
        PersistedDeviceIdentity? persisted;
        await using (var stream = File.OpenRead(_path))
        {
            try
            {
                persisted = await JsonSerializer.DeserializeAsync(
                    stream,
                    DeviceIdentityJsonContext.Default.PersistedDeviceIdentity,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"Device identity file '{_path}' is malformed. Delete it to regenerate a fresh identity.",
                    ex);
            }
        }

        if (persisted is null)
        {
            throw new InvalidOperationException(
                $"Device identity file '{_path}' is empty. Delete it to regenerate a fresh identity.");
        }

        var publicKey = Base64UrlEncoder.DecodeBytes(persisted.PublicKey);
        var privateKey = Base64UrlEncoder.DecodeBytes(persisted.PrivateKey);

        if (publicKey.Length != Ed25519Signer.PublicKeySize
            || privateKey.Length != Ed25519Signer.PrivateKeySeedSize)
        {
            throw new InvalidOperationException(
                $"Device identity file '{_path}' contains keys of the wrong size. Delete it to regenerate.");
        }

        _logger.LogInformation(
            "Loaded device identity from {Path} (deviceId={DeviceId})",
            _path, persisted.DeviceId);

        return new DeviceIdentity
        {
            DeviceId = persisted.DeviceId,
            PublicKey = publicKey,
            PrivateKey = privateKey,
            CreatedAtMs = persisted.CreatedAtMs,
            PairedAtMs = persisted.PairedAtMs,
        };
    }

    private async Task<DeviceIdentity> CreateAndPersistAsync(CancellationToken cancellationToken)
    {
        var (pub, priv) = Ed25519Signer.GenerateKeyPair();
        var identity = new DeviceIdentity
        {
            DeviceId = DeviceIdentity.ComputeDeviceId(pub),
            PublicKey = pub,
            PrivateKey = priv,
            CreatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            PairedAtMs = null,
        };

        await PersistAsync(identity, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Generated new device identity at {Path} (deviceId={DeviceId})",
            _path, identity.DeviceId);

        return identity;
    }

    private async Task PersistAsync(DeviceIdentity identity, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var persisted = new PersistedDeviceIdentity(
            DeviceId: identity.DeviceId,
            PublicKey: Base64UrlEncoder.Encode(identity.PublicKey),
            PrivateKey: Base64UrlEncoder.Encode(identity.PrivateKey),
            CreatedAtMs: identity.CreatedAtMs,
            PairedAtMs: identity.PairedAtMs);

        var tmp = _path + ".tmp";
        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                persisted,
                DeviceIdentityJsonContext.Default.PersistedDeviceIdentity,
                cancellationToken).ConfigureAwait(false);
        }

        File.Move(tmp, _path, overwrite: true);
        TrySetOwnerOnlyPermissions(_path);
    }

    private static void TrySetOwnerOnlyPermissions(string path)
    {
        try
        {
            // File.SetUnixFileMode is a no-op on Windows and 'unavailable' fails
            // loudly on some mounted filesystems (SMB/NFS); swallow so the
            // service still starts on exotic hosts.
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch (UnauthorizedAccessException) { /* best-effort */ }
        catch (IOException) { /* best-effort */ }
        catch (PlatformNotSupportedException) { /* older .NET on Windows */ }
    }
}
