using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Seren.Application.Abstractions;
using Seren.Application.Chat;
using Seren.Application.Tests.OpenClaw;
using Seren.Contracts.Events;
using Seren.Contracts.Events.Payloads;
using Seren.Domain.ValueObjects;
using Shouldly;
using Xunit;

namespace Seren.Application.Tests.Chat;

public sealed class LoadChatHistoryHandlerTests
{
    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly PeerId TargetPeer = PeerId.New();

    [Fact]
    public async Task Handle_WithFreshHydration_SendsBeginThenItemsThenEnd()
    {
        var ct = TestContext.Current.CancellationToken;
        var history = new FakeOpenClawHistory(messages:
        [
            new ChatHistoryMessage("m1", "user", "hello", 100, null),
            new ChatHistoryMessage("m2", "assistant", "hi back", 200, "joy"),
        ]);
        var hub = new FakeSerenHub();
        var handler = new LoadChatHistoryHandler(history, hub, NullLogger<LoadChatHistoryHandler>.Instance);

        await handler.Handle(new LoadChatHistoryCommand(TargetPeer, Before: null, Limit: 50), ct);

        hub.SendEnvelopes.Count.ShouldBe(4);
        hub.SendEnvelopes[0].Envelope.Type.ShouldBe(EventTypes.OutputChatHistoryBegin);
        hub.SendEnvelopes[1].Envelope.Type.ShouldBe(EventTypes.OutputChatHistoryItem);
        hub.SendEnvelopes[2].Envelope.Type.ShouldBe(EventTypes.OutputChatHistoryItem);
        hub.SendEnvelopes[3].Envelope.Type.ShouldBe(EventTypes.OutputChatHistoryEnd);

        var begin = JsonSerializer.Deserialize<ChatHistoryBeginPayload>(
            hub.SendEnvelopes[0].Envelope.Data.GetRawText(), CamelCase);
        begin!.Reset.ShouldBeTrue();

        var first = JsonSerializer.Deserialize<ChatHistoryItemPayload>(
            hub.SendEnvelopes[1].Envelope.Data.GetRawText(), CamelCase);
        first!.MessageId.ShouldBe("m1");
        first.Role.ShouldBe("user");

        var second = JsonSerializer.Deserialize<ChatHistoryItemPayload>(
            hub.SendEnvelopes[2].Envelope.Data.GetRawText(), CamelCase);
        second!.Emotion.ShouldBe("joy");

        var end = JsonSerializer.Deserialize<ChatHistoryEndPayload>(
            hub.SendEnvelopes[3].Envelope.Data.GetRawText(), CamelCase);
        end!.HasMore.ShouldBeFalse(); // upstream returned 2 < 50 → end of transcript
        end.OldestMessageId.ShouldBe("m1");

        history.LastFetchLimit.ShouldBe(50);
    }

    [Fact]
    public async Task Handle_WithBeforeCursor_FiltersOlderMessagesAndOverFetches()
    {
        var ct = TestContext.Current.CancellationToken;
        // Upstream returns oldest → newest, ids alphabetic so < comparison
        // mirrors chronological order for this synthetic scenario.
        var history = new FakeOpenClawHistory(messages:
        [
            new ChatHistoryMessage("a", "user", "old1", 1, null),
            new ChatHistoryMessage("b", "assistant", "old2", 2, null),
            new ChatHistoryMessage("c", "user", "mid", 3, null),
            new ChatHistoryMessage("d", "assistant", "recent", 4, null),
        ]);
        var hub = new FakeSerenHub();
        var handler = new LoadChatHistoryHandler(history, hub, NullLogger<LoadChatHistoryHandler>.Instance);

        // Page of 2 messages older than "c" → expect "a" and "b".
        await handler.Handle(new LoadChatHistoryCommand(TargetPeer, Before: "c", Limit: 2), ct);

        // Scroll-back must NOT emit history:begin — the client keeps the
        // already-visible page and splices older items at the top.
        hub.SendEnvelopes.ShouldNotContain(s => s.Envelope.Type == EventTypes.OutputChatHistoryBegin);

        var items = hub.SendEnvelopes
            .Where(s => s.Envelope.Type == EventTypes.OutputChatHistoryItem)
            .Select(s => JsonSerializer.Deserialize<ChatHistoryItemPayload>(
                s.Envelope.Data.GetRawText(), CamelCase)!)
            .ToList();

        items.Count.ShouldBe(2);
        items[0].MessageId.ShouldBe("a");
        items[1].MessageId.ShouldBe("b");
        history.LastFetchLimit.ShouldBe(LoadChatHistoryHandler.PaginationOverFetchFactor * 2);
    }

    [Fact]
    public async Task Handle_TargetsRequestingPeerOnly_NoBroadcast()
    {
        var ct = TestContext.Current.CancellationToken;
        var history = new FakeOpenClawHistory(messages:
        [
            new ChatHistoryMessage("m", "user", "hi", 1, null),
        ]);
        var hub = new FakeSerenHub();
        var handler = new LoadChatHistoryHandler(history, hub, NullLogger<LoadChatHistoryHandler>.Instance);

        await handler.Handle(new LoadChatHistoryCommand(TargetPeer, Before: null, Limit: 50), ct);

        hub.BroadcastEnvelopes.ShouldBeEmpty();
        hub.SendEnvelopes.ShouldAllBe(s => s.Peer == TargetPeer);
    }

    [Fact]
    public async Task Handle_WhenHistoryEmpty_StillSendsBeginAndEnd()
    {
        var ct = TestContext.Current.CancellationToken;
        var history = new FakeOpenClawHistory(messages: Array.Empty<ChatHistoryMessage>());
        var hub = new FakeSerenHub();
        var handler = new LoadChatHistoryHandler(history, hub, NullLogger<LoadChatHistoryHandler>.Instance);

        await handler.Handle(new LoadChatHistoryCommand(TargetPeer, Before: null, Limit: 50), ct);

        hub.SendEnvelopes.Count.ShouldBe(2);
        hub.SendEnvelopes[0].Envelope.Type.ShouldBe(EventTypes.OutputChatHistoryBegin);
        hub.SendEnvelopes[1].Envelope.Type.ShouldBe(EventTypes.OutputChatHistoryEnd);
        var end = JsonSerializer.Deserialize<ChatHistoryEndPayload>(
            hub.SendEnvelopes[1].Envelope.Data.GetRawText(), CamelCase);
        end!.HasMore.ShouldBeFalse();
        end.OldestMessageId.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_WhenHistoryThrows_StillEmitsBeginThenEmptyEnd()
    {
        // Upstream failure mustn't strand a reconnecting client with a
        // stale view: emit begin(reset) so the client drops local state,
        // then end(hasMore=false) so it stops spinning.
        var ct = TestContext.Current.CancellationToken;
        var history = new FakeOpenClawHistory(throwOnLoad: true);
        var hub = new FakeSerenHub();
        var handler = new LoadChatHistoryHandler(history, hub, NullLogger<LoadChatHistoryHandler>.Instance);

        await handler.Handle(new LoadChatHistoryCommand(TargetPeer, Before: null, Limit: 50), ct);

        hub.SendEnvelopes.Count.ShouldBe(2);
        hub.SendEnvelopes[0].Envelope.Type.ShouldBe(EventTypes.OutputChatHistoryBegin);
        hub.SendEnvelopes[1].Envelope.Type.ShouldBe(EventTypes.OutputChatHistoryEnd);
    }

    [Fact]
    public async Task Handle_WhenHistoryThrowsOnScrollBack_DoesNotEmitBegin()
    {
        // Scroll-back failure mustn't wipe the visible page either —
        // emitting begin(reset) here would throw away what the user is
        // already reading.
        var ct = TestContext.Current.CancellationToken;
        var history = new FakeOpenClawHistory(throwOnLoad: true);
        var hub = new FakeSerenHub();
        var handler = new LoadChatHistoryHandler(history, hub, NullLogger<LoadChatHistoryHandler>.Instance);

        await handler.Handle(new LoadChatHistoryCommand(TargetPeer, Before: "cursor", Limit: 30), ct);

        hub.SendEnvelopes.Count.ShouldBe(1);
        hub.SendEnvelopes[0].Envelope.Type.ShouldBe(EventTypes.OutputChatHistoryEnd);
    }

    [Fact]
    public async Task Handle_WithNonPositiveLimit_DoesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var history = new FakeOpenClawHistory(messages: Array.Empty<ChatHistoryMessage>());
        var hub = new FakeSerenHub();
        var handler = new LoadChatHistoryHandler(history, hub, NullLogger<LoadChatHistoryHandler>.Instance);

        await handler.Handle(new LoadChatHistoryCommand(TargetPeer, Before: null, Limit: 0), ct);

        hub.SendEnvelopes.ShouldBeEmpty();
        history.LoadCallCount.ShouldBe(0);
    }

    private sealed class FakeOpenClawHistory : IOpenClawHistory
    {
        private readonly IReadOnlyList<ChatHistoryMessage> _messages;
        private readonly bool _throwOnLoad;

        public FakeOpenClawHistory(
            IReadOnlyList<ChatHistoryMessage>? messages = null,
            bool throwOnLoad = false)
        {
            _messages = messages ?? Array.Empty<ChatHistoryMessage>();
            _throwOnLoad = throwOnLoad;
        }

        public int LoadCallCount { get; private set; }
        public int? LastFetchLimit { get; private set; }
        public int ResetCallCount { get; private set; }

        public Task<IReadOnlyList<ChatHistoryMessage>> LoadAsync(int limit, CancellationToken cancellationToken)
        {
            LoadCallCount++;
            LastFetchLimit = limit;
            if (_throwOnLoad)
            {
                throw new InvalidOperationException("simulated upstream failure");
            }
            return Task.FromResult(_messages);
        }

        public Task ResetAsync(CancellationToken cancellationToken)
        {
            ResetCallCount++;
            return Task.CompletedTask;
        }
    }
}
