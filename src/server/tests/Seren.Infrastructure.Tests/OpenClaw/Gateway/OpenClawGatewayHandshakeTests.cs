using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Seren.Infrastructure.OpenClaw;
using Seren.Infrastructure.OpenClaw.Gateway;
using Shouldly;
using Xunit;

namespace Seren.Infrastructure.Tests.OpenClaw.Gateway;

public sealed class OpenClawGatewayHandshakeTests
{
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

    [Fact]
    public async Task PerformAsync_SendsConnectRequest_WithProtocolAndClientIdentity()
    {
        await using var socket = new FakeGatewaySocket();
        EnqueueStandardHelloOk(socket, connectId => connectId);

        var hello = await OpenClawGatewayHandshake.PerformAsync(
            socket, OptionsWithToken, "9.9.9", TimeSpan.FromSeconds(5),
            NullLogger.Instance, CancellationToken.None);

        socket.SentFrames.Count.ShouldBe(1);
        using var sent = JsonDocument.Parse(socket.SentFrames[0]);
        sent.RootElement.GetProperty("type").GetString().ShouldBe("req");
        sent.RootElement.GetProperty("method").GetString().ShouldBe("connect");

        var p = sent.RootElement.GetProperty("params");
        p.GetProperty("minProtocol").GetInt32().ShouldBe(3);
        p.GetProperty("maxProtocol").GetInt32().ShouldBe(3);
        p.GetProperty("client").GetProperty("id").GetString().ShouldBe("gateway-client");
        p.GetProperty("client").GetProperty("mode").GetString().ShouldBe("backend");
        p.GetProperty("client").GetProperty("version").GetString().ShouldBe("9.9.9");
        p.GetProperty("auth").GetProperty("token").GetString().ShouldBe("shared-secret");
        p.GetProperty("role").GetString().ShouldBe("operator");

        hello.Protocol.ShouldBe(3);
        hello.Server.ConnId.ShouldBe("conn-xyz");
    }

    [Fact]
    public async Task PerformAsync_OmitsAuthBlock_WhenTokenNotConfigured()
    {
        await using var socket = new FakeGatewaySocket();
        EnqueueStandardHelloOk(socket, connectId => connectId);

        await OpenClawGatewayHandshake.PerformAsync(
            socket, OptionsWithoutToken, "1.0.0", TimeSpan.FromSeconds(5),
            NullLogger.Instance, CancellationToken.None);

        using var sent = JsonDocument.Parse(socket.SentFrames[0]);
        sent.RootElement.GetProperty("params").TryGetProperty("auth", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task PerformAsync_IgnoresConnectChallengeEventBeforeResponse()
    {
        await using var socket = new FakeGatewaySocket();
        socket.EnqueueServerFrame(
            """{"type":"event","event":"connect.challenge","payload":{"nonce":"n-1","ts":0}}""");
        EnqueueStandardHelloOk(socket, connectId => connectId);

        var hello = await OpenClawGatewayHandshake.PerformAsync(
            socket, OptionsWithToken, "1.0.0", TimeSpan.FromSeconds(5),
            NullLogger.Instance, CancellationToken.None);

        hello.ShouldNotBeNull();
    }

    [Fact]
    public async Task PerformAsync_Throws_WhenServerReturnsErrorResponse()
    {
        await using var socket = new FakeGatewaySocket();
        EnqueueErrorResponse(socket, "INVALID_REQUEST", "invalid connect params");

        var ex = await Should.ThrowAsync<OpenClawGatewayException>(async () =>
            await OpenClawGatewayHandshake.PerformAsync(
                socket, OptionsWithToken, "1.0.0", TimeSpan.FromSeconds(5),
                NullLogger.Instance, CancellationToken.None));

        ex.Code.ShouldBe("INVALID_REQUEST");
        ex.Message.ShouldBe("invalid connect params");
    }

    [Fact]
    public async Task PerformAsync_Throws_OnTimeout()
    {
        await using var socket = new FakeGatewaySocket();
        // No server frame enqueued → the receive will block indefinitely.

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await OpenClawGatewayHandshake.PerformAsync(
                socket, OptionsWithToken, "1.0.0", TimeSpan.FromMilliseconds(150),
                NullLogger.Instance, CancellationToken.None));
    }

    [Fact]
    public async Task PerformAsync_Throws_WhenServerClosesBeforeResponse()
    {
        await using var socket = new FakeGatewaySocket();
        socket.EnqueueServerClose(System.Net.WebSockets.WebSocketCloseStatus.PolicyViolation, "handshake timeout");

        var ex = await Should.ThrowAsync<OpenClawGatewayException>(async () =>
            await OpenClawGatewayHandshake.PerformAsync(
                socket, OptionsWithToken, "1.0.0", TimeSpan.FromSeconds(5),
                NullLogger.Instance, CancellationToken.None));

        ex.Code.ShouldBe("handshake.closed");
    }

    private static void EnqueueStandardHelloOk(FakeGatewaySocket socket, Func<string, string> idFromSent)
    {
        // Trick: we don't know the connect id upfront. Instead, we watch SentFrames
        // in the test by enqueuing via a task after the handshake sends its first
        // frame. Using TaskRun avoids awaiting before the send completes.
        _ = Task.Run(async () =>
        {
            while (socket.SentFrames.Count == 0)
            {
                await Task.Delay(5);
            }
            using var doc = JsonDocument.Parse(socket.SentFrames[0]);
            var id = idFromSent(doc.RootElement.GetProperty("id").GetString()!);
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
}
