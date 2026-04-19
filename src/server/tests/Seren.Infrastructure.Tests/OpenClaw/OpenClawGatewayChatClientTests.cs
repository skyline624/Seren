using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Seren.Application.Abstractions;
using Seren.Infrastructure.OpenClaw;
using Seren.Infrastructure.OpenClaw.Gateway;
using Shouldly;
using Xunit;

namespace Seren.Infrastructure.Tests.OpenClaw;

public sealed class OpenClawGatewayChatClientTests
{
    private static OpenClawGatewayChatClient BuildClient(
        IOpenClawGateway gateway,
        OpenClawChatStreamDispatcher dispatcher)
    {
        return new OpenClawGatewayChatClient(
            gateway,
            dispatcher,
            Options.Create(new OpenClawOptions()),
            NullLogger<OpenClawGatewayChatClient>.Instance);
    }

    private static OpenClawChatStreamDispatcher NewDispatcher() =>
        new(
            NullLogger<OpenClawChatStreamDispatcher>.Instance,
            runTtl: TimeSpan.FromMinutes(5),
            sweepInterval: TimeSpan.FromMinutes(1));

    private static ChatEventPayload Delta(string runId, string text) =>
        new(
            RunId: runId,
            SessionKey: "sess",
            Seq: 0,
            State: ChatEventState.Delta,
            Message: new ChatEventMessage(
                Role: "assistant",
                Content: new[] { new ChatEventMessageContent("text", text) },
                Timestamp: 0),
            StopReason: null,
            ErrorMessage: null,
            ErrorKind: null);

    private static ChatEventPayload Final(string runId, string text, string? stopReason = "stop") =>
        new(
            RunId: runId,
            SessionKey: "sess",
            Seq: 0,
            State: ChatEventState.Final,
            Message: new ChatEventMessage(
                Role: "assistant",
                Content: new[] { new ChatEventMessageContent("text", text) },
                Timestamp: 0),
            StopReason: stopReason,
            ErrorMessage: null,
            ErrorKind: null);

    [Fact]
    public async Task StartAsync_CallsChatSend_AndReturnsRunId()
    {
        var ct = TestContext.Current.CancellationToken;
        var gateway = Substitute.For<IOpenClawGateway>();
        gateway.CallAsync("chat.send", Arg.Any<object?>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(JsonSerializer.SerializeToElement(new { runId = "abc123", status = "started" }));

        await using var dispatcher = NewDispatcher();
        var client = BuildClient(gateway, dispatcher);

        var runId = await client.StartAsync("sess", "hello", agentId: null, ct);

        runId.ShouldBe("abc123");
        await gateway.Received(1).CallAsync(
            "chat.send",
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>(),
            Arg.Any<TimeSpan?>());
    }

    [Fact]
    public async Task StartAsync_Throws_WhenGatewayReturnsNoRunId()
    {
        var ct = TestContext.Current.CancellationToken;
        var gateway = Substitute.For<IOpenClawGateway>();
        gateway.CallAsync("chat.send", Arg.Any<object?>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(JsonSerializer.SerializeToElement(new { status = "started" }));

        await using var dispatcher = NewDispatcher();
        var client = BuildClient(gateway, dispatcher);

        var ex = await Should.ThrowAsync<OpenClawGatewayException>(async () =>
            await client.StartAsync("sess", "hi", null, ct));
        ex.Code.ShouldBe("chat.send.invalid");
    }

    [Fact]
    public async Task SubscribeAsync_ConvertsCumulativeTextToDeltas()
    {
        var ct = TestContext.Current.CancellationToken;
        var gateway = Substitute.For<IOpenClawGateway>();
        await using var dispatcher = NewDispatcher();
        var client = BuildClient(gateway, dispatcher);

        var produced = new List<string>();
        var consumer = Task.Run(async () =>
        {
            await foreach (var chunk in client.SubscribeAsync("run-1", ct))
            {
                if (chunk.Content is not null)
                {
                    produced.Add(chunk.Content);
                }
            }
        }, ct);

        // Give the consumer a moment to register its subscription.
        await Task.Delay(50, ct);

        dispatcher.Dispatch(Delta("run-1", "Hello"));
        dispatcher.Dispatch(Delta("run-1", "Hello, wor"));
        dispatcher.Dispatch(Delta("run-1", "Hello, world"));
        dispatcher.Dispatch(Final("run-1", "Hello, world!"));

        await consumer;

        string.Concat(produced).ShouldBe("Hello, world!");
    }

    [Fact]
    public async Task SubscribeAsync_DoesNotDuplicateText_WhenFinalEchoesLastDelta()
    {
        // Upstream flushes the last delta before emitting `final`; the final's
        // message usually echoes the same cumulative text. The delta computer
        // must treat that echo as "no new content" instead of re-yielding the
        // whole text. Regression for the "Hey there! 👋Hey there! 👋" bug.
        var ct = TestContext.Current.CancellationToken;
        var gateway = Substitute.For<IOpenClawGateway>();
        await using var dispatcher = NewDispatcher();
        var client = BuildClient(gateway, dispatcher);

        var produced = new List<string>();
        var consumer = Task.Run(async () =>
        {
            await foreach (var chunk in client.SubscribeAsync("run-echo", ct))
            {
                if (chunk.Content is not null)
                {
                    produced.Add(chunk.Content);
                }
            }
        }, ct);

        await Task.Delay(50, ct);
        dispatcher.Dispatch(Delta("run-echo", "Hey there! 👋"));
        dispatcher.Dispatch(Final("run-echo", "Hey there! 👋"));

        await consumer;
        string.Concat(produced).ShouldBe("Hey there! 👋");
    }

    [Fact]
    public async Task SubscribeAsync_YieldsFinalWithFinishReason()
    {
        var ct = TestContext.Current.CancellationToken;
        var gateway = Substitute.For<IOpenClawGateway>();
        await using var dispatcher = NewDispatcher();
        var client = BuildClient(gateway, dispatcher);

        ChatStreamDelta? terminal = null;
        var consumer = Task.Run(async () =>
        {
            await foreach (var chunk in client.SubscribeAsync("run-2", ct))
            {
                if (chunk.FinishReason is not null)
                {
                    terminal = chunk;
                }
            }
        }, ct);

        await Task.Delay(50, ct);
        dispatcher.Dispatch(Final("run-2", "done", stopReason: "length"));

        await consumer;
        terminal.ShouldNotBeNull();
        terminal!.FinishReason.ShouldBe("length");
    }

    [Fact]
    public async Task SubscribeAsync_ThrowsOnErrorState()
    {
        var ct = TestContext.Current.CancellationToken;
        var gateway = Substitute.For<IOpenClawGateway>();
        await using var dispatcher = NewDispatcher();
        var client = BuildClient(gateway, dispatcher);

        var consumer = Task.Run(async () =>
        {
            await foreach (var _ in client.SubscribeAsync("run-err", ct))
            {
                // no-op — error should surface as exception before any yield
            }
        }, ct);

        await Task.Delay(50, ct);
        dispatcher.Dispatch(new ChatEventPayload(
            RunId: "run-err",
            SessionKey: "sess",
            Seq: 0,
            State: ChatEventState.Error,
            Message: null,
            StopReason: null,
            ErrorMessage: "provider exploded",
            ErrorKind: "unknown"));

        var ex = await Should.ThrowAsync<OpenClawGatewayException>(async () => await consumer);
        ex.Code.ShouldBe("unknown");
        ex.Message.ShouldContain("provider exploded");
    }

    [Fact]
    public async Task SubscribeAsync_RaisesCanceledOnAbort()
    {
        var ct = TestContext.Current.CancellationToken;
        var gateway = Substitute.For<IOpenClawGateway>();
        await using var dispatcher = NewDispatcher();
        var client = BuildClient(gateway, dispatcher);

        var consumer = Task.Run(async () =>
        {
            await foreach (var _ in client.SubscribeAsync("run-abort", ct))
            {
            }
        }, ct);

        await Task.Delay(50, ct);
        dispatcher.Dispatch(new ChatEventPayload(
            RunId: "run-abort",
            SessionKey: "sess",
            Seq: 0,
            State: ChatEventState.Aborted,
            Message: null,
            StopReason: null,
            ErrorMessage: null,
            ErrorKind: null));

        await Should.ThrowAsync<OperationCanceledException>(async () => await consumer);
    }

    [Fact]
    public async Task SubscribeAsync_UnregistersRunOnCancellation()
    {
        var ct = TestContext.Current.CancellationToken;
        var gateway = Substitute.For<IOpenClawGateway>();
        await using var dispatcher = NewDispatcher();
        var client = BuildClient(gateway, dispatcher);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var consumer = Task.Run(async () =>
        {
            try
            {
                await foreach (var _ in client.SubscribeAsync("run-cancel", cts.Token))
                {
                }
            }
            catch (OperationCanceledException)
            {
                // expected when the token fires
            }
        }, ct);

        await Task.Delay(50, ct);
        dispatcher.RegisteredCount.ShouldBe(1);

        await cts.CancelAsync();
        await consumer;

        dispatcher.RegisteredCount.ShouldBe(0);
    }
}
