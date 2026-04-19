using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Seren.Infrastructure.OpenClaw;
using Seren.Infrastructure.OpenClaw.Gateway;
using Seren.Infrastructure.OpenClaw.Identity;
using Shouldly;
using Xunit;

namespace Seren.Infrastructure.Tests.OpenClaw.Gateway;

public sealed class OpenClawGatewayHandshakeTests
{
    private const string ChallengeNonce = "challenge-nonce-xyz";

    private static readonly OpenClawOptions OptionsWithToken = new()
    {
        BaseUrl = "ws://openclaw:18789",
        AuthToken = "shared-secret",
        DefaultAgentId = "ollama/qwen3",
    };

    private static readonly OpenClawOptions OptionsWithoutToken = new()
    {
        BaseUrl = "ws://localhost:18789",
        AuthToken = "",
        DefaultAgentId = "ollama/qwen3",
    };

    private static readonly OpenClawOptions OptionsWithBootstrap = new()
    {
        BaseUrl = "ws://openclaw:18789",
        AuthToken = "shared-secret",
        DefaultAgentId = "ollama/qwen3",
        BootstrapToken = "one-shot-pairing-token",
    };

    [Fact]
    public async Task PerformAsync_SendsConnectRequest_WithDeviceBlockAndValidSignature()
    {
        await using var socket = new FakeGatewaySocket();
        socket.EnqueueServerFrame(ChallengeFrame(ChallengeNonce));
        EnqueueStandardHelloOk(socket);

        var identityStore = new FakeDeviceIdentityStore();

        var hello = await OpenClawGatewayHandshake.PerformAsync(
            socket, OptionsWithToken, identityStore, "9.9.9", TimeSpan.FromSeconds(5),
            NullLogger.Instance, CancellationToken.None);

        socket.SentFrames.Count.ShouldBe(1);
        using var sent = JsonDocument.Parse(socket.SentFrames[0]);
        sent.RootElement.GetProperty("type").GetString().ShouldBe("req");
        sent.RootElement.GetProperty("method").GetString().ShouldBe("connect");

        var p = sent.RootElement.GetProperty("params");
        p.GetProperty("client").GetProperty("id").GetString().ShouldBe("gateway-client");
        p.GetProperty("client").GetProperty("mode").GetString().ShouldBe("backend");
        p.GetProperty("auth").GetProperty("token").GetString().ShouldBe("shared-secret");
        p.GetProperty("role").GetString().ShouldBe("operator");

        // Device block present with the expected fields.
        var device = p.GetProperty("device");
        device.GetProperty("id").GetString().ShouldBe(identityStore.Identity.DeviceId);
        device.GetProperty("publicKey").GetString()
            .ShouldBe(Base64UrlEncoder.Encode(identityStore.Identity.PublicKey));
        device.GetProperty("nonce").GetString().ShouldBe(ChallengeNonce);
        device.GetProperty("signedAt").GetInt64().ShouldBeGreaterThan(0);
        var signature = Base64UrlEncoder.DecodeBytes(device.GetProperty("signature").GetString()!);

        // Reproduce the V3 payload the handshake just signed and verify the
        // signature lines up with the public key we know.
        var expectedPayload = DeviceAuthPayloadBuilder.BuildV3(
            deviceId: identityStore.Identity.DeviceId,
            clientId: "gateway-client",
            clientMode: "backend",
            role: "operator",
            scopes: OpenClawGatewayProtocol.BackendOperatorScopes,
            signedAtMs: device.GetProperty("signedAt").GetInt64(),
            token: "shared-secret",
            nonce: ChallengeNonce,
            platform: p.GetProperty("client").GetProperty("platform").GetString(),
            deviceFamily: null);

        Ed25519Signer.Verify(identityStore.Identity.PublicKey, expectedPayload, signature).ShouldBeTrue();

        hello.Protocol.ShouldBe(3);
        hello.Server.ConnId.ShouldBe("conn-xyz");
    }

    [Fact]
    public async Task PerformAsync_BootstrapMode_IncludesBootstrapTokenAndUsesNodeRoleWithEmptyScopes()
    {
        await using var socket = new FakeGatewaySocket();
        socket.EnqueueServerFrame(ChallengeFrame(ChallengeNonce));
        EnqueueStandardHelloOk(socket);

        await OpenClawGatewayHandshake.PerformAsync(
            socket, OptionsWithBootstrap, new FakeDeviceIdentityStore(),
            "1.0.0", TimeSpan.FromSeconds(5),
            NullLogger.Instance, CancellationToken.None,
            OpenClawGatewayHandshake.HandshakeMode.BootstrapPairing);

        using var sent = JsonDocument.Parse(socket.SentFrames[0]);
        var p = sent.RootElement.GetProperty("params");
        p.GetProperty("auth").GetProperty("bootstrapToken").GetString().ShouldBe("one-shot-pairing-token");
        // OpenClaw's silent auto-approval predicate only fires when role=node + scopes empty.
        p.GetProperty("role").GetString().ShouldBe("node");
        p.TryGetProperty("scopes", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task PerformAsync_StandardMode_OmitsBootstrapToken_EvenWhenConfigured()
    {
        // Standard mode never sends the bootstrap token: it is reserved for
        // the one-shot pairing handshake. This avoids accidentally consuming
        // the token on subsequent reconnects.
        await using var socket = new FakeGatewaySocket();
        socket.EnqueueServerFrame(ChallengeFrame(ChallengeNonce));
        EnqueueStandardHelloOk(socket);

        await OpenClawGatewayHandshake.PerformAsync(
            socket, OptionsWithBootstrap, new FakeDeviceIdentityStore(),
            "1.0.0", TimeSpan.FromSeconds(5),
            NullLogger.Instance, CancellationToken.None);

        using var sent = JsonDocument.Parse(socket.SentFrames[0]);
        var auth = sent.RootElement.GetProperty("params").GetProperty("auth");
        auth.TryGetProperty("bootstrapToken", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task PerformAsync_OmitsBootstrapToken_WhenNotConfigured()
    {
        await using var socket = new FakeGatewaySocket();
        socket.EnqueueServerFrame(ChallengeFrame(ChallengeNonce));
        EnqueueStandardHelloOk(socket);

        await OpenClawGatewayHandshake.PerformAsync(
            socket, OptionsWithToken, new FakeDeviceIdentityStore(),
            "1.0.0", TimeSpan.FromSeconds(5),
            NullLogger.Instance, CancellationToken.None);

        using var sent = JsonDocument.Parse(socket.SentFrames[0]);
        var auth = sent.RootElement.GetProperty("params").GetProperty("auth");
        auth.TryGetProperty("bootstrapToken", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task PerformAsync_UsesFallbackNonce_WhenChallengeNeverArrives()
    {
        await using var socket = new FakeGatewaySocket();
        // Skip the challenge frame entirely — test short-circuits the 5s wait
        // via the fast challenge timeout (see ChallengeTimeout field).
        EnqueueStandardHelloOk(socket);

        await OpenClawGatewayHandshake.PerformAsync(
            socket, OptionsWithToken, new FakeDeviceIdentityStore(),
            "1.0.0", TimeSpan.FromSeconds(30),
            NullLogger.Instance, CancellationToken.None);

        using var sent = JsonDocument.Parse(socket.SentFrames[0]);
        var nonce = sent.RootElement.GetProperty("params").GetProperty("device").GetProperty("nonce").GetString();
        nonce.ShouldNotBeNullOrEmpty();
        // Locally-generated fallback is a 32-char GUID hex.
        nonce!.Length.ShouldBe(32);
    }

    [Fact]
    public async Task PerformAsync_OmitsAuthToken_WhenTokenNotConfigured()
    {
        await using var socket = new FakeGatewaySocket();
        socket.EnqueueServerFrame(ChallengeFrame(ChallengeNonce));
        EnqueueStandardHelloOk(socket);

        await OpenClawGatewayHandshake.PerformAsync(
            socket, OptionsWithoutToken, new FakeDeviceIdentityStore(),
            "1.0.0", TimeSpan.FromSeconds(5),
            NullLogger.Instance, CancellationToken.None);

        using var sent = JsonDocument.Parse(socket.SentFrames[0]);
        // auth block is always present now (device-related fields may live there),
        // but token itself should be omitted when not configured.
        sent.RootElement.GetProperty("params")
            .TryGetProperty("auth", out var auth)
            .ShouldBeTrue();
        auth.TryGetProperty("token", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task PerformAsync_Throws_WhenServerReturnsErrorResponse()
    {
        await using var socket = new FakeGatewaySocket();
        socket.EnqueueServerFrame(ChallengeFrame(ChallengeNonce));
        EnqueueErrorResponse(socket, "INVALID_REQUEST", "invalid connect params");

        var ex = await Should.ThrowAsync<OpenClawGatewayException>(async () =>
            await OpenClawGatewayHandshake.PerformAsync(
                socket, OptionsWithToken, new FakeDeviceIdentityStore(),
                "1.0.0", TimeSpan.FromSeconds(5),
                NullLogger.Instance, CancellationToken.None));

        ex.Code.ShouldBe("INVALID_REQUEST");
        ex.Message.ShouldBe("invalid connect params");
    }

    [Fact]
    public async Task PerformAsync_AppendsBootstrapHint_WhenNotPairedWithoutBootstrap()
    {
        await using var socket = new FakeGatewaySocket();
        socket.EnqueueServerFrame(ChallengeFrame(ChallengeNonce));
        EnqueueErrorResponse(socket, "NOT_PAIRED", "device identity required");

        var ex = await Should.ThrowAsync<OpenClawGatewayException>(async () =>
            await OpenClawGatewayHandshake.PerformAsync(
                socket, OptionsWithToken, new FakeDeviceIdentityStore(),
                "1.0.0", TimeSpan.FromSeconds(5),
                NullLogger.Instance, CancellationToken.None));

        ex.Code.ShouldBe("NOT_PAIRED");
        ex.Message.ShouldContain("OPENCLAW_BOOTSTRAP_TOKEN");
        ex.Message.ShouldContain("docker compose exec");
    }

    [Fact]
    public async Task PerformAsync_DoesNotAppendBootstrapHint_WhenBootstrapAlreadyConfigured()
    {
        await using var socket = new FakeGatewaySocket();
        socket.EnqueueServerFrame(ChallengeFrame(ChallengeNonce));
        EnqueueErrorResponse(socket, "NOT_PAIRED", "device identity rejected");

        var ex = await Should.ThrowAsync<OpenClawGatewayException>(async () =>
            await OpenClawGatewayHandshake.PerformAsync(
                socket, OptionsWithBootstrap, new FakeDeviceIdentityStore(),
                "1.0.0", TimeSpan.FromSeconds(5),
                NullLogger.Instance, CancellationToken.None));

        ex.Code.ShouldBe("NOT_PAIRED");
        ex.Message.ShouldBe("device identity rejected");
    }

    [Fact]
    public async Task PerformAsync_Throws_OnTimeout()
    {
        await using var socket = new FakeGatewaySocket();
        socket.EnqueueServerFrame(ChallengeFrame(ChallengeNonce));
        // No server response enqueued after the challenge — receive blocks.

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await OpenClawGatewayHandshake.PerformAsync(
                socket, OptionsWithToken, new FakeDeviceIdentityStore(),
                "1.0.0", TimeSpan.FromMilliseconds(200),
                NullLogger.Instance, CancellationToken.None));
    }

    [Fact]
    public async Task PerformAsync_Throws_WhenServerClosesBeforeResponse()
    {
        await using var socket = new FakeGatewaySocket();
        socket.EnqueueServerFrame(ChallengeFrame(ChallengeNonce));
        socket.EnqueueServerClose(System.Net.WebSockets.WebSocketCloseStatus.PolicyViolation, "handshake timeout");

        var ex = await Should.ThrowAsync<OpenClawGatewayException>(async () =>
            await OpenClawGatewayHandshake.PerformAsync(
                socket, OptionsWithToken, new FakeDeviceIdentityStore(),
                "1.0.0", TimeSpan.FromSeconds(5),
                NullLogger.Instance, CancellationToken.None));

        ex.Code.ShouldBe("handshake.closed");
    }

    private static string ChallengeFrame(string nonce) =>
        "{\"type\":\"event\",\"event\":\"connect.challenge\",\"payload\":{\"nonce\":\""
        + nonce + "\",\"ts\":0}}";

    private static void EnqueueStandardHelloOk(FakeGatewaySocket socket)
    {
        _ = Task.Run(async () =>
        {
            while (socket.SentFrames.Count == 0)
            {
                await Task.Delay(5);
            }
            using var doc = JsonDocument.Parse(socket.SentFrames[0]);
            var id = doc.RootElement.GetProperty("id").GetString()!;
            var helloOk = "{\"type\":\"res\",\"id\":\"" + id + "\",\"ok\":true,\"payload\":"
                + "{\"protocol\":3,"
                + "\"server\":{\"version\":\"1.2.3\",\"connId\":\"conn-xyz\"},"
                + "\"features\":{\"methods\":[\"chat.send\"],\"events\":[\"tick\"]},"
                + "\"policy\":{\"maxPayload\":524288,\"maxBufferedBytes\":1048576,\"tickIntervalMs\":5000}"
                + "}}";
            socket.EnqueueServerFrame(helloOk);
        });
    }

    private static void EnqueueErrorResponse(FakeGatewaySocket socket, string code, string message)
    {
        _ = Task.Run(async () =>
        {
            while (socket.SentFrames.Count == 0)
            {
                await Task.Delay(5);
            }
            using var doc = JsonDocument.Parse(socket.SentFrames[0]);
            var id = doc.RootElement.GetProperty("id").GetString()!;
            var err = "{\"type\":\"res\",\"id\":\"" + id + "\",\"ok\":false,"
                + "\"error\":{\"code\":\"" + code + "\",\"message\":\"" + message + "\"}}";
            socket.EnqueueServerFrame(err);
        });
    }

    /// <summary>
    /// Deterministic in-memory identity store: creates one keypair on
    /// construction and returns it forever.
    /// </summary>
    private sealed class FakeDeviceIdentityStore : IDeviceIdentityStore
    {
        public FakeDeviceIdentityStore()
        {
            var (pub, priv) = Ed25519Signer.GenerateKeyPair();
            Identity = new DeviceIdentity
            {
                DeviceId = DeviceIdentity.ComputeDeviceId(pub),
                PublicKey = pub,
                PrivateKey = priv,
                CreatedAtMs = 1_700_000_000_000,
            };
        }

        public DeviceIdentity Identity { get; }
        public bool MarkPairedCalled { get; private set; }

        public Task<DeviceIdentity> LoadOrCreateAsync(CancellationToken cancellationToken)
            => Task.FromResult(Identity);

        public Task MarkPairedAsync(CancellationToken cancellationToken)
        {
            MarkPairedCalled = true;
            return Task.CompletedTask;
        }
    }
}
