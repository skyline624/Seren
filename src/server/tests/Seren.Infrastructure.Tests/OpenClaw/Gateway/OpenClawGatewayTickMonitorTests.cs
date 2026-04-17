using System.Net.WebSockets;
using Microsoft.Extensions.Logging.Abstractions;
using Seren.Infrastructure.OpenClaw.Gateway;
using Shouldly;
using Xunit;

namespace Seren.Infrastructure.Tests.OpenClaw.Gateway;

public sealed class OpenClawGatewayTickMonitorTests
{
    [Fact]
    public async Task CloseFires_WhenNoFrameWithinGraceWindow()
    {
        var tcs = new TaskCompletionSource<(WebSocketCloseStatus, string)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await using var monitor = new OpenClawGatewayTickMonitor(
            tickIntervalMs: 100,
            graceMultiplier: 2.0,
            closeAsync: (status, reason, _) =>
            {
                tcs.TrySetResult((status, reason));
                return Task.CompletedTask;
            },
            logger: NullLogger.Instance);

        // Don't feed any frames — monitor should fire after ~200 ms.
        var completed = await Task.WhenAny(
            tcs.Task,
            Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        completed.ShouldBe(tcs.Task);

        var (status, reason) = await tcs.Task;
        ((int)status).ShouldBe(4000);
        reason.ShouldBe("tick timeout");
    }

    [Fact]
    public async Task CloseNotFired_WhenFramesArriveRegularly()
    {
        var fireCount = 0;

        await using var monitor = new OpenClawGatewayTickMonitor(
            tickIntervalMs: 100,
            graceMultiplier: 2.0,
            closeAsync: (_, _, _) => { Interlocked.Increment(ref fireCount); return Task.CompletedTask; },
            logger: NullLogger.Instance);

        for (var i = 0; i < 6; i++)
        {
            monitor.OnFrameReceived();
            await Task.Delay(80, TestContext.Current.CancellationToken);
        }

        fireCount.ShouldBe(0);
    }

    [Fact]
    public void Constructor_Throws_OnInvalidGraceMultiplier()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new OpenClawGatewayTickMonitor(
                tickIntervalMs: 100,
                graceMultiplier: 1.0,
                closeAsync: (_, _, _) => Task.CompletedTask,
                logger: NullLogger.Instance));
    }

    [Fact]
    public void Constructor_Throws_OnNonPositiveTickInterval()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new OpenClawGatewayTickMonitor(
                tickIntervalMs: 0,
                graceMultiplier: 2.0,
                closeAsync: (_, _, _) => Task.CompletedTask,
                logger: NullLogger.Instance));
    }
}
