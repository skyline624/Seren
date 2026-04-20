using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Seren.Application.Abstractions;
using Seren.Application.Chat;
using Seren.Application.Tests.OpenClaw;
using Seren.Contracts.Events;
using Seren.Contracts.Events.Payloads;
using Shouldly;
using Xunit;

namespace Seren.Application.Tests.Chat;

public sealed class ResetChatSessionHandlerTests
{
    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public async Task Handle_RotatesSessionKey_AndBroadcastsCleared()
    {
        var ct = TestContext.Current.CancellationToken;
        var sessionKey = new RotatingSessionKey("seren-main");
        var hub = new FakeSerenHub();
        var handler = new ResetChatSessionHandler(sessionKey, hub, NullLogger<ResetChatSessionHandler>.Instance);

        await handler.Handle(new ResetChatSessionCommand(), ct);

        sessionKey.RotateCallCount.ShouldBe(1);
        sessionKey.MainSessionKey.ShouldBe("seren-main-g1");
        hub.BroadcastEnvelopes.Count.ShouldBe(1);
        hub.BroadcastEnvelopes[0].Type.ShouldBe(EventTypes.OutputChatCleared);

        var payload = JsonSerializer.Deserialize<ChatClearedPayload>(
            hub.BroadcastEnvelopes[0].Data.GetRawText(), CamelCase);
        payload!.At.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Handle_BroadcastsToAllPeers_NoExclusion()
    {
        var ct = TestContext.Current.CancellationToken;
        var sessionKey = new RotatingSessionKey("seren-main");
        var hub = new RecordingExcludingHub();
        var handler = new ResetChatSessionHandler(sessionKey, hub, NullLogger<ResetChatSessionHandler>.Instance);

        await handler.Handle(new ResetChatSessionCommand(), ct);

        hub.LastExclusion.ShouldBeNull();
    }

    private sealed class RotatingSessionKey : IChatSessionKeyProvider
    {
        private readonly string _baseKey;
        private int _gen;

        public RotatingSessionKey(string baseKey)
        {
            _baseKey = baseKey;
            MainSessionKey = baseKey;
        }

        public string MainSessionKey { get; private set; }
        public int RotateCallCount { get; private set; }

        public Task<string> RotateAsync(CancellationToken cancellationToken)
        {
            RotateCallCount++;
            _gen++;
            MainSessionKey = $"{_baseKey}-g{_gen}";
            return Task.FromResult(MainSessionKey);
        }
    }

    private sealed class RecordingExcludingHub : ISerenHub
    {
        public Domain.ValueObjects.PeerId? LastExclusion { get; private set; }

        public Task<bool> SendAsync(
            Domain.ValueObjects.PeerId peerId,
            WebSocketEnvelope envelope,
            CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<int> BroadcastAsync(
            WebSocketEnvelope envelope,
            Domain.ValueObjects.PeerId? excluding,
            CancellationToken cancellationToken)
        {
            LastExclusion = excluding;
            return Task.FromResult(0);
        }
    }
}
