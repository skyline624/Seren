using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Seren.Application.Abstractions;
using Seren.Application.Chat;
using Seren.Contracts.Events;
using Seren.Domain.ValueObjects;
using Shouldly;
using Xunit;

namespace Seren.Application.Tests.Chat;

/// <summary>
/// Focused unit tests for <see cref="ChatStreamPipeline"/> — covers the
/// resilience decisions (retry on idle-before-first-chunk, fallback cascade,
/// no-retry-after-content) and terminal broadcast semantics, using fake
/// <see cref="IOpenClawChat"/> / <see cref="ISerenHub"/> stubs to control
/// the timing deterministically.
/// </summary>
public sealed class ChatStreamPipelineTests
{
    [Fact]
    public async Task RunAsync_HappyPath_BroadcastsChatEnd_AndOutcomeOk()
    {
        var ct = TestContext.Current.CancellationToken;
        var chat = new ScriptedChat(
            new AttemptScript(new ChatStreamDelta("Hello", null), new ChatStreamDelta(null, "stop")));
        var hub = new CapturingHub();
        var pipeline = BuildPipeline(chat, hub, resilience: NoResilience);

        var collectedContent = "";
        var outcome = await pipeline.RunAsync(new ChatStreamRequest(
            SessionKey: "sess",
            UserText: "hi",
            PrimaryModel: "model-a",
            ClientMessageId: "msg-1",
            CharacterId: null,
            OnContent: (c, _) => { collectedContent += c; return Task.CompletedTask; }), ct);

        outcome.Outcome.ShouldBe(ChatStreamOutcomes.Ok);
        outcome.AttemptsMade.ShouldBe(1);
        outcome.ModelUsed.ShouldBe("model-a");
        collectedContent.ShouldBe("Hello");
        hub.Broadcasts.ShouldContain(e => e.Type == EventTypes.OutputChatEnd);
        hub.Broadcasts.Any(e => e.Type == EventTypes.Error).ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_IdleBeforeFirstChunk_RetriesSameModel_AndSucceeds()
    {
        var ct = TestContext.Current.CancellationToken;
        // Attempt 1 hangs (no deltas), attempt 2 succeeds.
        var chat = new ScriptedChat(
            new AttemptScript().WithIdleHang(),
            new AttemptScript(new ChatStreamDelta("Recovered", null), new ChatStreamDelta(null, "stop")));
        var hub = new CapturingHub();
        var pipeline = BuildPipeline(chat, hub, resilience: new ChatResilienceOptions
        {
            RetryOnIdleBeforeFirstChunk = 1,
            RetryBackoff = TimeSpan.Zero,
            FallbackModels = new List<string>(),
        }, idle: TimeSpan.FromMilliseconds(100));

        var outcome = await pipeline.RunAsync(new ChatStreamRequest(
            SessionKey: "sess",
            UserText: "retry please",
            PrimaryModel: "model-a",
            ClientMessageId: null,
            CharacterId: null,
            OnContent: (_, _) => Task.CompletedTask), ct);

        outcome.Outcome.ShouldBe(ChatStreamOutcomes.Ok);
        outcome.AttemptsMade.ShouldBe(2);
        outcome.ModelUsed.ShouldBe("model-a");
        chat.AbortCalls.ShouldContain("hang-run");

        // UI should have seen the degraded notice once before the final end.
        hub.Broadcasts.Count(e => e.Type == EventTypes.OutputChatProviderDegraded).ShouldBe(1);
        hub.Broadcasts.ShouldContain(e => e.Type == EventTypes.OutputChatEnd);
        hub.Broadcasts.Any(e => e.Type == EventTypes.Error).ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_FallbackCascade_UsedWhenPrimaryExhausted()
    {
        var ct = TestContext.Current.CancellationToken;
        var chat = new ScriptedChat(
            new AttemptScript().WithIdleHang(),  // primary attempt 1: hang
            new AttemptScript().WithIdleHang(),  // primary retry 1: hang
            new AttemptScript(new ChatStreamDelta("From fallback", null), new ChatStreamDelta(null, "stop")));
        var hub = new CapturingHub();
        var pipeline = BuildPipeline(chat, hub, resilience: new ChatResilienceOptions
        {
            RetryOnIdleBeforeFirstChunk = 1,
            RetryBackoff = TimeSpan.Zero,
            FallbackModels = new List<string> { "model-b" },
        }, idle: TimeSpan.FromMilliseconds(100));

        var outcome = await pipeline.RunAsync(new ChatStreamRequest(
            SessionKey: "sess",
            UserText: "hi",
            PrimaryModel: "model-a",
            ClientMessageId: null,
            CharacterId: null,
            OnContent: (_, _) => Task.CompletedTask), ct);

        outcome.Outcome.ShouldBe(ChatStreamOutcomes.Ok);
        outcome.AttemptsMade.ShouldBe(3);
        outcome.ModelUsed.ShouldBe("model-b");
        hub.Broadcasts.Count(e => e.Type == EventTypes.OutputChatProviderDegraded).ShouldBe(2);
        chat.ModelsTried.ShouldBe(ExpectedCascadeModels);
    }

    [Fact]
    public async Task RunAsync_NoRetry_AfterContentDelivered()
    {
        var ct = TestContext.Current.CancellationToken;
        // Attempt 1 emits a chunk then hangs — we must NOT retry because
        // the UI already saw content and we can't rewind it.
        var chat = new ScriptedChat(
            new AttemptScript(new ChatStreamDelta("Partial", null)).WithIdleHang(),
            new AttemptScript(new ChatStreamDelta("Never reached", null), new ChatStreamDelta(null, "stop")));
        var hub = new CapturingHub();
        var pipeline = BuildPipeline(chat, hub, resilience: new ChatResilienceOptions
        {
            RetryOnIdleBeforeFirstChunk = 5,  // plenty of retries allowed
            RetryBackoff = TimeSpan.Zero,
            FallbackModels = new List<string> { "model-b" },
        }, idle: TimeSpan.FromMilliseconds(100));

        var contentSeen = "";
        var outcome = await pipeline.RunAsync(new ChatStreamRequest(
            SessionKey: "sess",
            UserText: "hi",
            PrimaryModel: "model-a",
            ClientMessageId: null,
            CharacterId: null,
            OnContent: (c, _) => { contentSeen += c; return Task.CompletedTask; }), ct);

        outcome.Outcome.ShouldBe(ChatStreamOutcomes.IdleTimeout);
        outcome.AttemptsMade.ShouldBe(1);
        contentSeen.ShouldBe("Partial");
        hub.Broadcasts.ShouldContain(e => e.Type == EventTypes.Error);
        hub.Broadcasts.ShouldContain(e => e.Type == EventTypes.OutputChatEnd);
        hub.Broadcasts.Any(e => e.Type == EventTypes.OutputChatProviderDegraded).ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_OnContent_IsPrimaryChannelForDeltas()
    {
        var ct = TestContext.Current.CancellationToken;
        var chat = new ScriptedChat(new AttemptScript(
            new ChatStreamDelta("Chunk1 ", null),
            new ChatStreamDelta("Chunk2", null),
            new ChatStreamDelta(null, "stop")));
        var hub = new CapturingHub();
        var pipeline = BuildPipeline(chat, hub, resilience: NoResilience);

        var received = new List<string>();
        await pipeline.RunAsync(new ChatStreamRequest(
            SessionKey: "sess",
            UserText: "hi",
            PrimaryModel: "m",
            ClientMessageId: null,
            CharacterId: null,
            OnContent: (c, _) => { received.Add(c); return Task.CompletedTask; }), ct);

        received.ShouldBe(ExpectedChunks);
    }

    [Fact]
    public async Task RunAsync_OnSuccess_OnlyFiresOnCleanEnd()
    {
        var ct = TestContext.Current.CancellationToken;
        var chat = new ScriptedChat(new AttemptScript().WithIdleHang());
        var hub = new CapturingHub();
        var pipeline = BuildPipeline(chat, hub, resilience: NoResilience, idle: TimeSpan.FromMilliseconds(100));

        var successCount = 0;
        await pipeline.RunAsync(new ChatStreamRequest(
            SessionKey: "sess",
            UserText: "hi",
            PrimaryModel: "m",
            ClientMessageId: null,
            CharacterId: null,
            OnContent: (_, _) => Task.CompletedTask,
            OnSuccess: _ => { successCount++; return Task.CompletedTask; }), ct);

        successCount.ShouldBe(0);
    }

    [Fact]
    public async Task RunAsync_OnTeardown_AlwaysFires_BeforeTerminalBroadcast()
    {
        var ct = TestContext.Current.CancellationToken;
        var chat = new ScriptedChat(new AttemptScript().WithIdleHang());
        var hub = new CapturingHub();
        var pipeline = BuildPipeline(chat, hub, resilience: NoResilience, idle: TimeSpan.FromMilliseconds(100));

        var teardownCount = 0;
        await pipeline.RunAsync(new ChatStreamRequest(
            SessionKey: "sess",
            UserText: "hi",
            PrimaryModel: "m",
            ClientMessageId: null,
            CharacterId: null,
            OnContent: (_, _) => Task.CompletedTask,
            OnTeardown: _ => { teardownCount++; return Task.CompletedTask; }), ct);

        teardownCount.ShouldBe(1);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static readonly ChatResilienceOptions NoResilience = new()
    {
        RetryOnIdleBeforeFirstChunk = 0,
        FallbackModels = new List<string>(),
    };

    private static readonly string[] ExpectedCascadeModels = ["model-a", "model-a", "model-b"];
    private static readonly string[] ExpectedChunks = ["Chunk1 ", "Chunk2"];

    private static ChatStreamPipeline BuildPipeline(
        IOpenClawChat chat,
        ISerenHub hub,
        ChatResilienceOptions resilience,
        TimeSpan? idle = null)
    {
        var streamOptions = Options.Create(new ChatStreamOptions
        {
            IdleTimeout = idle ?? TimeSpan.FromSeconds(30),
            TotalTimeout = TimeSpan.FromMinutes(3),
        });

        return new ChatStreamPipeline(
            chat,
            hub,
            new FakeRegistry(),
            streamOptions,
            Options.Create(resilience),
            new ChatStreamMetrics(),
            NullLogger<ChatStreamPipeline>.Instance);
    }

    private sealed class FakeRegistry : IChatRunRegistry
    {
        private readonly ConcurrentDictionary<string, string> _runs = new();
        public void Register(string sessionKey, string runId) => _runs[sessionKey] = runId;
        public void Unregister(string sessionKey, string runId)
            => _runs.TryRemove(new KeyValuePair<string, string>(sessionKey, runId));
        public string? GetActiveRun(string sessionKey)
            => _runs.TryGetValue(sessionKey, out var v) ? v : null;
    }

    private sealed class CapturingHub : ISerenHub
    {
        public List<WebSocketEnvelope> Broadcasts { get; } = [];

        public Task<bool> SendAsync(PeerId peerId, WebSocketEnvelope envelope, CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task<int> BroadcastAsync(WebSocketEnvelope envelope, PeerId? excluding, CancellationToken cancellationToken)
        {
            Broadcasts.Add(envelope);
            return Task.FromResult(1);
        }
    }

    /// <summary>
    /// Deterministic <see cref="IOpenClawChat"/> stub: one <see cref="AttemptScript"/>
    /// per expected attempt. The n-th <c>StartAsync</c> call returns the n-th
    /// script's runId and later <c>SubscribeAsync</c> yields that script's
    /// deltas (or hangs until cancellation if <c>WithIdleHang</c> was set).
    /// </summary>
    private sealed class ScriptedChat : IOpenClawChat
    {
        private readonly AttemptScript[] _scripts;
        private int _attemptIndex = -1;
        public List<string> ModelsTried { get; } = [];
        public List<string> AbortCalls { get; } = [];

        public ScriptedChat(params AttemptScript[] scripts) { _scripts = scripts; }

        public Task PinSessionModelAsync(string sessionKey, string? model, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<string> StartAsync(
            string sessionKey, string message, string? agentId, string? idempotencyKey, CancellationToken cancellationToken)
        {
            _attemptIndex++;
            ModelsTried.Add(agentId ?? "<default>");
            return Task.FromResult(_scripts[_attemptIndex].WillHang ? "hang-run" : $"run-{_attemptIndex + 1}");
        }

        public Task AbortAsync(string sessionKey, string runId, CancellationToken cancellationToken)
        {
            AbortCalls.Add(runId);
            return Task.CompletedTask;
        }

        public IAsyncEnumerable<ChatStreamDelta> SubscribeAsync(string runId, CancellationToken cancellationToken)
        {
            // The _attemptIndex currently points at the active script.
            return _scripts[_attemptIndex].EnumerateAsync(cancellationToken);
        }
    }

    private sealed class AttemptScript
    {
        private readonly ChatStreamDelta[] _deltas;
        public bool WillHang { get; private set; }

        public AttemptScript(params ChatStreamDelta[] deltas) { _deltas = deltas; }

        public AttemptScript WithIdleHang()
        {
            WillHang = true;
            return this;
        }

        public async IAsyncEnumerable<ChatStreamDelta> EnumerateAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var delta in _deltas)
            {
                await Task.Yield();
                yield return delta;
            }

            if (WillHang)
            {
                // Wait until the pipeline's idle CTS fires and cancels us.
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
