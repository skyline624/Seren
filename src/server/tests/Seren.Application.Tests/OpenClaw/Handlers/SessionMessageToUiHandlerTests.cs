using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Seren.Application.OpenClaw.Handlers;
using Seren.Application.OpenClaw.Notifications;
using Seren.Contracts.Events;
using Seren.Contracts.Events.Payloads;
using Shouldly;
using Xunit;

namespace Seren.Application.Tests.OpenClaw.Handlers;

public sealed class SessionMessageToUiHandlerTests
{
    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public async Task Handle_BroadcastsSessionMessageEnvelope()
    {
        var ct = TestContext.Current.CancellationToken;
        var hub = new FakeSerenHub();
        var handler = new SessionMessageToUiHandler(hub, NullLogger<SessionMessageToUiHandler>.Instance);

        var notification = new SessionMessageReceivedNotification(
            SessionKey: "sess-1",
            Role: "user",
            Content: "hello from discord",
            Timestamp: 1712345678000,
            Author: "alice",
            Channel: "discord",
            Seq: 7);

        await handler.Handle(notification, ct);

        hub.BroadcastEnvelopes.Count.ShouldBe(1);
        hub.BroadcastEnvelopes[0].Type.ShouldBe(EventTypes.OutputSessionMessage);

        var payload = JsonSerializer.Deserialize<SessionMessagePayload>(
            hub.BroadcastEnvelopes[0].Data.GetRawText(), CamelCase);
        payload!.SessionKey.ShouldBe("sess-1");
        payload.Role.ShouldBe("user");
        payload.Content.ShouldBe("hello from discord");
        payload.Author.ShouldBe("alice");
        payload.Channel.ShouldBe("discord");
        payload.Seq.ShouldBe(7);
    }
}
