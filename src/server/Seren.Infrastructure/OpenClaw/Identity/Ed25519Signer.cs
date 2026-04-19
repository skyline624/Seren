using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace Seren.Infrastructure.OpenClaw.Identity;

/// <summary>
/// Thin Ed25519 wrapper: generate keypair, sign, verify. Built on
/// <c>BouncyCastle.Cryptography</c> so we don't need libsodium in the
/// runtime image.
/// </summary>
/// <remarks>
/// Ed25519 uses a 32-byte secret seed from which the expanded private key is
/// derived; this class always handles the raw 32-byte seed form — the shape
/// that matches OpenClaw's <c>loadOrCreateDeviceIdentity</c> output.
/// </remarks>
internal static class Ed25519Signer
{
    /// <summary>Ed25519 public key size (raw, not SPKI).</summary>
    public const int PublicKeySize = 32;

    /// <summary>Ed25519 private seed size (from which the expanded key is derived).</summary>
    public const int PrivateKeySeedSize = 32;

    /// <summary>Ed25519 signature size.</summary>
    public const int SignatureSize = 64;

    /// <summary>
    /// Create a fresh Ed25519 keypair backed by the system's CSPRNG.
    /// </summary>
    public static (byte[] PublicKey, byte[] PrivateKeySeed) GenerateKeyPair()
    {
        var seed = RandomNumberGenerator.GetBytes(PrivateKeySeedSize);
        var privateParams = new Ed25519PrivateKeyParameters(seed, 0);
        var publicParams = privateParams.GeneratePublicKey();
        return (publicParams.GetEncoded(), seed);
    }

    /// <summary>
    /// Sign <paramref name="message"/> with the provided 32-byte private seed.
    /// </summary>
    public static byte[] Sign(byte[] privateKeySeed, byte[] message)
    {
        ArgumentNullException.ThrowIfNull(privateKeySeed);
        ArgumentNullException.ThrowIfNull(message);
        if (privateKeySeed.Length != PrivateKeySeedSize)
        {
            throw new ArgumentException(
                $"Ed25519 private seed must be exactly {PrivateKeySeedSize} bytes (got {privateKeySeed.Length}).",
                nameof(privateKeySeed));
        }

        var privateParams = new Ed25519PrivateKeyParameters(privateKeySeed, 0);
        var signer = SignerUtilities.GetSigner("Ed25519");
        signer.Init(forSigning: true, privateParams);
        signer.BlockUpdate(message, 0, message.Length);
        return signer.GenerateSignature();
    }

    /// <summary>Convenience overload for UTF-8 string payloads.</summary>
    public static byte[] Sign(byte[] privateKeySeed, string message) =>
        Sign(privateKeySeed, Encoding.UTF8.GetBytes(message));

    /// <summary>
    /// Verify a signature. Returns <c>false</c> on any mismatch or bad
    /// input size — never throws on routine validation failure.
    /// </summary>
    public static bool Verify(byte[] publicKey, byte[] message, byte[] signature)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(signature);
        if (publicKey.Length != PublicKeySize || signature.Length != SignatureSize)
        {
            return false;
        }

        var publicParams = new Ed25519PublicKeyParameters(publicKey, 0);
        var verifier = SignerUtilities.GetSigner("Ed25519");
        verifier.Init(forSigning: false, publicParams);
        verifier.BlockUpdate(message, 0, message.Length);
        return verifier.VerifySignature(signature);
    }

    /// <summary>Convenience overload for UTF-8 string payloads.</summary>
    public static bool Verify(byte[] publicKey, string message, byte[] signature) =>
        Verify(publicKey, Encoding.UTF8.GetBytes(message), signature);
}
