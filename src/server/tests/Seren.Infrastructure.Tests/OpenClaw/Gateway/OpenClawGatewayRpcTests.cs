using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Seren.Infrastructure.OpenClaw.Gateway;
using Shouldly;
using Xunit;

namespace Seren.Infrastructure.Tests.OpenClaw.Gateway;

public sealed class OpenClawGatewayRpcTests
{
    [Fact]
    public async Task CallAsync_ResolvesPayload_WhenResponseMatchesId()
    {
        await using var rpc = new OpenClawGatewayRpc(NullLogger.Instance, TimeSpan.FromSeconds(5));
        GatewayRequest? captured = null;

        var callTask = rpc.CallAsync(
            sendAsync: (req, _) => { captured = req; return Task.CompletedTask; },
            method: "sessions.list",
            parameters: null,
            timeout: null,
            cancellationToken: CancellationToken.None);

        captured.ShouldNotBeNull();
        rpc.PendingCount.ShouldBe(1);

        var resp = new GatewayResponse(
            Id: captured!.Id,
            Ok: true,
            Payload: JsonSerializer.SerializeToElement(new { count = 3 }),
            Error: null);
        rpc.CompletePending(resp).ShouldBeTrue();

        var payload = await callTask;
        payload.GetProperty("count").GetInt32().ShouldBe(3);
        rpc.PendingCount.ShouldBe(0);
    }

    [Fact]
    public async Task CallAsync_Throws_OnServerErrorResponse()
    {
        await using var rpc = new OpenClawGatewayRpc(NullLogger.Instance, TimeSpan.FromSeconds(5));
        GatewayRequest? captured = null;

        var callTask = rpc.CallAsync(
            sendAsync: (req, _) => { captured = req; return Task.CompletedTask; },
            method: "chat.history",
            parameters: new { sessionId = "s1" },
            timeout: null,
            cancellationToken: CancellationToken.None);

        rpc.CompletePending(new GatewayResponse(
            Id: captured!.Id,
            Ok: false,
            Payload: null,
            Error: new GatewayError("NOT_FOUND", "no such session", null, false, null)));

        var ex = await Should.ThrowAsync<OpenClawGatewayException>(async () => await callTask);
        ex.Code.ShouldBe("NOT_FOUND");
        ex.Message.ShouldBe("no such session");
    }

    [Fact]
    public async Task CallAsync_Throws_WhenTimeoutExpires()
    {
        await using var rpc = new OpenClawGatewayRpc(NullLogger.Instance, TimeSpan.FromSeconds(30));

        var callTask = rpc.CallAsync(
            sendAsync: (_, _) => Task.CompletedTask,
            method: "slow.method",
            parameters: null,
            timeout: TimeSpan.FromMilliseconds(100),
            cancellationToken: CancellationToken.None);

        await Should.ThrowAsync<OperationCanceledException>(async () => await callTask);
        rpc.PendingCount.ShouldBe(0);
    }

    [Fact]
    public async Task CallAsync_Throws_WhenCallerCancels()
    {
        await using var rpc = new OpenClawGatewayRpc(NullLogger.Instance, TimeSpan.FromSeconds(30));
        using var cts = new CancellationTokenSource();

        var callTask = rpc.CallAsync(
            sendAsync: (_, _) => Task.CompletedTask,
            method: "slow.method",
            parameters: null,
            timeout: null,
            cancellationToken: cts.Token);

        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(async () => await callTask);
        rpc.PendingCount.ShouldBe(0);
    }

    [Fact]
    public async Task CompletePending_ReturnsFalse_OnUnknownId()
    {
        await using var rpc = new OpenClawGatewayRpc(NullLogger.Instance, TimeSpan.FromSeconds(5));
        var fake = new GatewayResponse("does-not-exist", true, null, null);

        rpc.CompletePending(fake).ShouldBeFalse();
    }

    [Fact]
    public async Task FailAllPending_FailsEveryInFlightCall()
    {
        await using var rpc = new OpenClawGatewayRpc(NullLogger.Instance, TimeSpan.FromSeconds(30));

        var t1 = rpc.CallAsync(
            sendAsync: (_, _) => Task.CompletedTask,
            method: "a", parameters: null, timeout: null,
            cancellationToken: CancellationToken.None);
        var t2 = rpc.CallAsync(
            sendAsync: (_, _) => Task.CompletedTask,
            method: "b", parameters: null, timeout: null,
            cancellationToken: CancellationToken.None);

        rpc.PendingCount.ShouldBe(2);
        rpc.FailAllPending(new InvalidOperationException("connection dropped"));

        await Should.ThrowAsync<InvalidOperationException>(async () => await t1);
        await Should.ThrowAsync<InvalidOperationException>(async () => await t2);
        rpc.PendingCount.ShouldBe(0);
    }

    [Fact]
    public void Constructor_Throws_WhenTimeoutNotPositive()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new OpenClawGatewayRpc(NullLogger.Instance, TimeSpan.Zero));
    }
}
