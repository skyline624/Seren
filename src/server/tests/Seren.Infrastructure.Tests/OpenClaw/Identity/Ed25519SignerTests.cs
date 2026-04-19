using System.Text;
using Seren.Infrastructure.OpenClaw.Identity;
using Shouldly;
using Xunit;

namespace Seren.Infrastructure.Tests.OpenClaw.Identity;

public sealed class Ed25519SignerTests
{
    [Fact]
    public void GenerateKeyPair_ReturnsExactlySizedRawKeys()
    {
        var (pub, priv) = Ed25519Signer.GenerateKeyPair();

        pub.Length.ShouldBe(Ed25519Signer.PublicKeySize);
        priv.Length.ShouldBe(Ed25519Signer.PrivateKeySeedSize);
    }

    [Fact]
    public void GenerateKeyPair_ProducesDistinctKeypairsEachCall()
    {
        var (pubA, privA) = Ed25519Signer.GenerateKeyPair();
        var (pubB, privB) = Ed25519Signer.GenerateKeyPair();

        pubA.ShouldNotBe(pubB);
        privA.ShouldNotBe(privB);
    }

    [Fact]
    public void SignThenVerify_Roundtrips()
    {
        var (pub, priv) = Ed25519Signer.GenerateKeyPair();
        var message = "v3|dev-test|gateway-client|backend|operator|operator.write|42|tok|nonce||"u8.ToArray();

        var sig = Ed25519Signer.Sign(priv, message);
        sig.Length.ShouldBe(Ed25519Signer.SignatureSize);
        Ed25519Signer.Verify(pub, message, sig).ShouldBeTrue();
    }

    [Fact]
    public void Verify_ReturnsFalse_WhenMessageTampered()
    {
        var (pub, priv) = Ed25519Signer.GenerateKeyPair();
        var original = Encoding.UTF8.GetBytes("canonical message");
        var tampered = Encoding.UTF8.GetBytes("canonical messagf"); // last char changed

        var sig = Ed25519Signer.Sign(priv, original);
        Ed25519Signer.Verify(pub, tampered, sig).ShouldBeFalse();
    }

    [Fact]
    public void Verify_ReturnsFalse_WhenSignatureTampered()
    {
        var (pub, priv) = Ed25519Signer.GenerateKeyPair();
        var message = Encoding.UTF8.GetBytes("payload");
        var sig = Ed25519Signer.Sign(priv, message);

        // Flip one bit in the signature.
        sig[0] ^= 0x01;
        Ed25519Signer.Verify(pub, message, sig).ShouldBeFalse();
    }

    [Fact]
    public void Verify_ReturnsFalse_WhenVerifiedWithDifferentKey()
    {
        var (_, privA) = Ed25519Signer.GenerateKeyPair();
        var (pubB, _) = Ed25519Signer.GenerateKeyPair();
        var message = Encoding.UTF8.GetBytes("msg");

        var sig = Ed25519Signer.Sign(privA, message);
        Ed25519Signer.Verify(pubB, message, sig).ShouldBeFalse();
    }

    [Fact]
    public void Sign_IsDeterministic_ForSameKeyAndMessage()
    {
        // Ed25519 is deterministic (RFC 8032 §5.1.6): same (k, M) → same σ.
        var (_, priv) = Ed25519Signer.GenerateKeyPair();
        var message = Encoding.UTF8.GetBytes("same message");

        var sigA = Ed25519Signer.Sign(priv, message);
        var sigB = Ed25519Signer.Sign(priv, message);

        sigA.ShouldBe(sigB);
    }

    [Fact]
    public void Sign_Throws_OnWrongSeedSize()
    {
        var badSeed = new byte[16];
        Should.Throw<ArgumentException>(() =>
            Ed25519Signer.Sign(badSeed, Encoding.UTF8.GetBytes("x")));
    }

    [Fact]
    public void Verify_ReturnsFalse_OnWrongPublicKeySize()
    {
        var (_, priv) = Ed25519Signer.GenerateKeyPair();
        var sig = Ed25519Signer.Sign(priv, "msg");
        var badPub = new byte[16];

        Ed25519Signer.Verify(badPub, Encoding.UTF8.GetBytes("msg"), sig).ShouldBeFalse();
    }
}
