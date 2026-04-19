using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Seren.Application.OpenClaw.Handlers;
using Seren.Application.OpenClaw.Notifications;
using Seren.Contracts.Events;
using Seren.Contracts.Events.Payloads;
using Shouldly;
using Xunit;

namespace Seren.Application.Tests.OpenClaw.Handlers;

public sealed class AgentEventToUiHandlerTests
{
    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public async Task Handle_RelaysToolStream()
    {
        var ct = TestContext.Current.CancellationToken;
        var hub = new FakeSerenHub();
        var handler = new AgentEventToUiHandler(hub, NullLogger<AgentEventToUiHandler>.Instance);

        var data = JsonSerializer.SerializeToElement(new { phase = "start", name = "fetch_data" });

        await handler.Handle(new AgentEventNotification(
            RunId: "run-1",
            SessionKey: "sess",
            Stream: "tool",
            Phase: "start",
            Seq: 3,
            Data: data), ct);

        hub.BroadcastEnvelopes.Count.ShouldBe(1);
        hub.BroadcastEnvelopes[0].Type.ShouldBe(EventTypes.OutputAgentEvent);

        var payload = JsonSerializer.Deserialize<AgentEventPayload>(
            hub.BroadcastEnvelopes[0].Data.GetRawText(), CamelCase);
        payload!.RunId.ShouldBe("run-1");
        payload.Stream.ShouldBe("tool");
        payload.Phase.ShouldBe("start");
    }

    [Fact]
    public async Task Handle_RelaysItemStream()
    {
        var hub = new FakeSerenHub();
        var handler = new AgentEventToUiHandler(hub, NullLogger<AgentEventToUiHandler>.Instance);

        await handler.Handle(new AgentEventNotification(
            RunId: "run-2",
            SessionKey: null,
            Stream: "item",
            Phase: "end",
            Seq: null,
            Data: null), CancellationToken.None);

        hub.BroadcastEnvelopes.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_SkipsUnknownStream()
    {
        var hub = new FakeSerenHub();
        var handler = new AgentEventToUiHandler(hub, NullLogger<AgentEventToUiHandler>.Instance);

        await handler.Handle(new AgentEventNotification(
            RunId: "run-3",
            SessionKey: null,
            Stream: "telemetry",
            Phase: "tick",
            Seq: null,
            Data: null), CancellationToken.None);

        hub.BroadcastEnvelopes.ShouldBeEmpty();
    }
}
