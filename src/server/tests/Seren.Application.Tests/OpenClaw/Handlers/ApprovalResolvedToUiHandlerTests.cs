using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Seren.Application.OpenClaw.Handlers;
using Seren.Application.OpenClaw.Notifications;
using Seren.Contracts.Events;
using Seren.Contracts.Events.Payloads;
using Shouldly;
using Xunit;

namespace Seren.Application.Tests.OpenClaw.Handlers;

public sealed class ApprovalResolvedToUiHandlerTests
{
    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public async Task Handle_BroadcastsApprovalResolvedEnvelope()
    {
        var ct = TestContext.Current.CancellationToken;
        var hub = new FakeSerenHub();
        var handler = new ApprovalResolvedToUiHandler(hub, NullLogger<ApprovalResolvedToUiHandler>.Instance);

        await handler.Handle(new ApprovalResolvedNotification(
            Id: "appr-1",
            Kind: "exec",
            Decision: "allow",
            ResolvedBy: "admin",
            ResolvedAtMs: 1712346000000), CancellationToken.None);

        hub.BroadcastEnvelopes.Count.ShouldBe(1);
        hub.BroadcastEnvelopes[0].Type.ShouldBe(EventTypes.OutputApprovalResolved);

        var payload = JsonSerializer.Deserialize<ApprovalResolvedPayload>(
            hub.BroadcastEnvelopes[0].Data.GetRawText(), CamelCase);
        payload!.Id.ShouldBe("appr-1");
        payload.Kind.ShouldBe("exec");
        payload.Decision.ShouldBe("allow");
        payload.ResolvedBy.ShouldBe("admin");
    }
}
