using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Seren.Infrastructure.OpenClaw.Identity;

/// <summary>
/// Persistent Ed25519 identity Seren presents to the OpenClaw gateway at
/// every handshake so its self-declared scopes are preserved.
/// </summary>
/// <remarks>
/// Raw byte arrays are used (not PEM/SPKI) because the gateway expects
/// base64url-encoded 32-byte public keys and 64-byte signatures on the wire
/// (see <c>openclaw/src/gateway/device-auth.ts</c>). The <see cref="DeviceId"/>
/// is a hex fingerprint (SHA-256 over the raw public key) that upstream uses
/// to look up the paired record.
/// </remarks>
public sealed record DeviceIdentity
{
    /// <summary>Hex-encoded SHA-256 of the raw public key (64 characters, lowercase).</summary>
    public required string DeviceId { get; init; }

    /// <summary>Raw Ed25519 public key (exactly 32 bytes).</summary>
    public required byte[] PublicKey { get; init; }

    /// <summary>Raw Ed25519 private seed (exactly 32 bytes).</summary>
    public required byte[] PrivateKey { get; init; }

    /// <summary>Unix epoch milliseconds when the keypair was created.</summary>
    public long CreatedAtMs { get; init; }

    /// <summary>
    /// Unix epoch milliseconds when the bootstrap pairing flow completed
    /// successfully against OpenClaw — <c>null</c> means the keypair has
    /// never been paired and the bootstrap handshake should be attempted on
    /// the next boot if a token is configured.
    /// </summary>
    public long? PairedAtMs { get; init; }

    /// <summary>Base64url-encoded public key, the shape OpenClaw expects on the wire.</summary>
    public string PublicKeyBase64Url() => Base64UrlEncoder.Encode(PublicKey);

    /// <summary>
    /// Derive a device id from a raw public key: the gateway indexes paired
    /// devices by this hex fingerprint.
    /// </summary>
    public static string ComputeDeviceId(byte[] publicKeyRaw)
    {
        ArgumentNullException.ThrowIfNull(publicKeyRaw);
        var hash = SHA256.HashData(publicKeyRaw);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
