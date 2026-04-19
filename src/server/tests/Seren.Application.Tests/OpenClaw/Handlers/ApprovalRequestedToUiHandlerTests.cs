using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Seren.Application.OpenClaw.Handlers;
using Seren.Application.OpenClaw.Notifications;
using Seren.Contracts.Events;
using Seren.Contracts.Events.Payloads;
using Shouldly;
using Xunit;

namespace Seren.Application.Tests.OpenClaw.Handlers;

public sealed class ApprovalRequestedToUiHandlerTests
{
    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public async Task Handle_BroadcastsApprovalRequestEnvelope()
    {
        var ct = TestContext.Current.CancellationToken;
        var hub = new FakeSerenHub();
        var handler = new ApprovalRequestedToUiHandler(hub, NullLogger<ApprovalRequestedToUiHandler>.Instance);

        var notification = new ApprovalRequestedNotification(
            Id: "appr-1",
            Kind: "exec",
            Title: "Delete backups",
            Summary: "Destructive",
            Command: "rm -rf /data",
            CreatedAtMs: 1712345678000,
            ExpiresAtMs: 1712345978000,
            SourceChannel: "slack");

        await handler.Handle(notification, ct);

        hub.BroadcastEnvelopes.Count.ShouldBe(1);
        hub.BroadcastEnvelopes[0].Type.ShouldBe(EventTypes.OutputApprovalRequest);

        var payload = JsonSerializer.Deserialize<ApprovalRequestPayload>(
            hub.BroadcastEnvelopes[0].Data.GetRawText(), CamelCase);
        payload!.Id.ShouldBe("appr-1");
        payload.Kind.ShouldBe("exec");
        payload.Title.ShouldBe("Delete backups");
        payload.Command.ShouldBe("rm -rf /data");
        payload.SourceChannel.ShouldBe("slack");
    }

    [Fact]
    public async Task Handle_RelaysPluginKindFaithfully()
    {
        var ct = TestContext.Current.CancellationToken;
        var hub = new FakeSerenHub();
        var handler = new ApprovalRequestedToUiHandler(hub, NullLogger<ApprovalRequestedToUiHandler>.Instance);

        await handler.Handle(new ApprovalRequestedNotification(
            Id: "appr-2",
            Kind: "plugin",
            Title: "Install ext",
            Summary: null,
            Command: null,
            CreatedAtMs: null,
            ExpiresAtMs: null,
            SourceChannel: null), ct);

        var payload = JsonSerializer.Deserialize<ApprovalRequestPayload>(
            hub.BroadcastEnvelopes[0].Data.GetRawText(), CamelCase);
        payload!.Kind.ShouldBe("plugin");
    }
}
