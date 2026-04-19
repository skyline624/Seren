using Microsoft.Extensions.Logging.Abstractions;
using Seren.Infrastructure.OpenClaw.Gateway;
using Shouldly;
using Xunit;

namespace Seren.Infrastructure.Tests.OpenClaw.Gateway;

public sealed class OpenClawChatStreamDispatcherTests
{
    private static OpenClawChatStreamDispatcher NewDispatcher(
        TimeSpan? runTtl = null, TimeSpan? sweepInterval = null) =>
        new(
            NullLogger<OpenClawChatStreamDispatcher>.Instance,
            runTtl ?? TimeSpan.FromMinutes(5),
            sweepInterval ?? TimeSpan.FromMinutes(1));

    private static ChatEventPayload Delta(string runId, string text, long seq = 0) =>
        new(
            RunId: runId,
            SessionKey: "sess",
            Seq: seq,
            State: ChatEventState.Delta,
            Message: new ChatEventMessage(
                Role: "assistant",
                Content: new[] { new ChatEventMessageContent(Type: "text", Text: text) },
                Timestamp: 0),
            StopReason: null,
            ErrorMessage: null,
            ErrorKind: null);

    private static ChatEventPayload Final(string runId, string text, long seq = 0) =>
        new(
            RunId: runId,
            SessionKey: "sess",
            Seq: seq,
            State: ChatEventState.Final,
            Message: new ChatEventMessage(
                Role: "assistant",
                Content: new[] { new ChatEventMessageContent(Type: "text", Text: text) },
                Timestamp: 0),
            StopReason: "stop",
            ErrorMessage: null,
            ErrorKind: null);

    [Fact]
    public async Task Register_ThenDispatchDelta_DeliversToReader()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var dispatcher = NewDispatcher();
        var reader = dispatcher.Register("run-1");

        dispatcher.Dispatch(Delta("run-1", "hello")).ShouldBeTrue();

        var payload = await reader.ReadAsync(ct);
        payload.State.ShouldBe(ChatEventState.Delta);
        payload.Message!.Content![0].Text.ShouldBe("hello");
    }

    [Fact]
    public async Task Register_Throws_OnDuplicateRunId()
    {
        await using var dispatcher = NewDispatcher();
        _ = dispatcher.Register("run-1");

        Should.Throw<InvalidOperationException>(() => dispatcher.Register("run-1"));
    }

    [Fact]
    public async Task Dispatch_ReturnsFalse_ForUnknownRun()
    {
        await using var dispatcher = NewDispatcher();
        dispatcher.Dispatch(Delta("unknown-run", "x")).ShouldBeFalse();
    }

    [Fact]
    public async Task TerminalState_CompletesChannelAndRemovesRun()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var dispatcher = NewDispatcher();
        var reader = dispatcher.Register("run-2");

        dispatcher.Dispatch(Delta("run-2", "partial")).ShouldBeTrue();
        dispatcher.Dispatch(Final("run-2", "partial done")).ShouldBeTrue();

        var first = await reader.ReadAsync(ct);
        first.State.ShouldBe(ChatEventState.Delta);
        var second = await reader.ReadAsync(ct);
        second.State.ShouldBe(ChatEventState.Final);

        var completed = await reader.WaitToReadAsync(ct);
        completed.ShouldBeFalse();

        dispatcher.RegisteredCount.ShouldBe(0);
    }

    [Fact]
    public async Task AbortedState_CompletesChannelAndRemovesRun()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var dispatcher = NewDispatcher();
        var reader = dispatcher.Register("run-abort");

        var aborted = new ChatEventPayload(
            RunId: "run-abort",
            SessionKey: "sess",
            Seq: 5,
            State: ChatEventState.Aborted,
            Message: null,
            StopReason: null,
            ErrorMessage: null,
            ErrorKind: null);

        dispatcher.Dispatch(aborted).ShouldBeTrue();

        (await reader.ReadAsync(ct)).State.ShouldBe(ChatEventState.Aborted);
        (await reader.WaitToReadAsync(ct)).ShouldBeFalse();
        dispatcher.RegisteredCount.ShouldBe(0);
    }

    [Fact]
    public async Task ErrorState_CompletesChannelAndRemovesRun()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var dispatcher = NewDispatcher();
        var reader = dispatcher.Register("run-err");

        var err = new ChatEventPayload(
            RunId: "run-err",
            SessionKey: "sess",
            Seq: 0,
            State: ChatEventState.Error,
            Message: null,
            StopReason: null,
            ErrorMessage: "upstream exploded",
            ErrorKind: "unknown");

        dispatcher.Dispatch(err).ShouldBeTrue();

        (await reader.ReadAsync(ct)).ErrorMessage.ShouldBe("upstream exploded");
        (await reader.WaitToReadAsync(ct)).ShouldBeFalse();
        dispatcher.RegisteredCount.ShouldBe(0);
    }

    [Fact]
    public async Task Unregister_CompletesChannelAndEvictsRun()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var dispatcher = NewDispatcher();
        var reader = dispatcher.Register("run-cancel");
        dispatcher.RegisteredCount.ShouldBe(1);

        dispatcher.Unregister("run-cancel");

        (await reader.WaitToReadAsync(ct)).ShouldBeFalse();
        dispatcher.RegisteredCount.ShouldBe(0);
    }

    [Fact]
    public async Task Unregister_IsNoOp_ForUnknownRun()
    {
        await using var dispatcher = NewDispatcher();
        dispatcher.Unregister("never-registered"); // must not throw
    }

    [Fact]
    public async Task ConcurrentRuns_AreIsolated()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var dispatcher = NewDispatcher();
        var r1 = dispatcher.Register("r1");
        var r2 = dispatcher.Register("r2");

        dispatcher.Dispatch(Delta("r1", "one"));
        dispatcher.Dispatch(Delta("r2", "two"));
        dispatcher.Dispatch(Final("r1", "one done"));
        dispatcher.Dispatch(Final("r2", "two done"));

        (await r1.ReadAsync(ct)).Message!.Content![0].Text.ShouldBe("one");
        (await r1.ReadAsync(ct)).Message!.Content![0].Text.ShouldBe("one done");
        (await r2.ReadAsync(ct)).Message!.Content![0].Text.ShouldBe("two");
        (await r2.ReadAsync(ct)).Message!.Content![0].Text.ShouldBe("two done");
    }

    [Fact]
    public async Task SweepOnce_EvictsOrphanRunsBeyondTtl()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var dispatcher = NewDispatcher(runTtl: TimeSpan.FromMinutes(5));
        var reader = dispatcher.Register("orphan");

        dispatcher.SweepOnce(DateTimeOffset.UtcNow.AddMinutes(10));

        dispatcher.RegisteredCount.ShouldBe(0);
        var ex = await Should.ThrowAsync<OpenClawGatewayException>(
            async () => await reader.WaitToReadAsync(ct));
        ex.Code.ShouldBe("chat.stream.orphan");
    }

    [Fact]
    public async Task DisposeAsync_CompletesPendingReadersWithShutdownError()
    {
        var ct = TestContext.Current.CancellationToken;
        var dispatcher = NewDispatcher();
        var reader = dispatcher.Register("still-running");

        await dispatcher.DisposeAsync();

        var ex = await Should.ThrowAsync<OpenClawGatewayException>(
            async () => await reader.WaitToReadAsync(ct));
        ex.Code.ShouldBe("chat.stream.shutdown");
    }

    [Fact]
    public void Constructor_Throws_OnInvalidTtl()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new OpenClawChatStreamDispatcher(
                NullLogger<OpenClawChatStreamDispatcher>.Instance,
                runTtl: TimeSpan.Zero,
                sweepInterval: TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task Register_ThrowsAfterDispose()
    {
        var dispatcher = NewDispatcher();
        await dispatcher.DisposeAsync();
        Should.Throw<ObjectDisposedException>(() => dispatcher.Register("late"));
    }
}
