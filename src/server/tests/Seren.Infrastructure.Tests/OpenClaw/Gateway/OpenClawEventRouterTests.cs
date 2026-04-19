using System.Text.Json;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Seren.Application.OpenClaw.Notifications;
using Seren.Infrastructure.OpenClaw.Gateway;
using Shouldly;
using Xunit;

namespace Seren.Infrastructure.Tests.OpenClaw.Gateway;

public sealed class OpenClawEventRouterTests
{
    private static (OpenClawEventRouter router, OpenClawChatStreamDispatcher dispatcher, IPublisher publisher)
        BuildRouter(IPublisher? overridePublisher = null)
    {
        var publisher = overridePublisher ?? Substitute.For<IPublisher>();
        var dispatcher = new OpenClawChatStreamDispatcher(
            NullLogger<OpenClawChatStreamDispatcher>.Instance,
            runTtl: TimeSpan.FromMinutes(5),
            sweepInterval: TimeSpan.FromMinutes(1));
        var services = new ServiceCollection();
        services.AddSingleton(publisher);
        var sp = services.BuildServiceProvider();
        var router = new OpenClawEventRouter(
            dispatcher,
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OpenClawEventRouter>.Instance);
        return (router, dispatcher, publisher);
    }

    private static OpenClawGatewayRawEventNotification Notif(string eventName, object payload, long? seq = null) =>
        new(eventName, JsonSerializer.SerializeToElement(payload), seq);

    [Fact]
    public async Task ChatEvent_IsRoutedToDispatcher_AndNotPublished()
    {
        var ct = TestContext.Current.CancellationToken;
        var (router, dispatcher, publisher) = BuildRouter();
        var reader = dispatcher.Register("run-x");

        await router.Handle(Notif("chat", new
        {
            runId = "run-x",
            sessionKey = "sess",
            seq = 0,
            state = "delta",
            message = new
            {
                role = "assistant",
                content = new object[] { new { type = "text", text = "hi" } },
                timestamp = 0,
            },
        }), ct);

        (await reader.ReadAsync(ct)).Message!.Content![0].Text.ShouldBe("hi");
        await publisher.DidNotReceiveWithAnyArgs().Publish<INotification>(default!, ct);
    }

    [Fact]
    public async Task ChatEvent_WithoutRunId_IsDropped()
    {
        var ct = TestContext.Current.CancellationToken;
        var (router, dispatcher, _) = BuildRouter();
        var reader = dispatcher.Register("run-other");

        await router.Handle(Notif("chat", new { state = "delta" }), ct);

        dispatcher.RegisteredCount.ShouldBe(1);
        reader.TryRead(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task SessionMessage_IsPublishedAsTypedNotification()
    {
        var ct = TestContext.Current.CancellationToken;
        var (router, _, publisher) = BuildRouter();

        await router.Handle(Notif("session.message", new
        {
            sessionKey = "sess-42",
            messageSeq = 7,
            message = new
            {
                role = "user",
                content = "hello world",
                timestamp = 1712345678000L,
                author = "alice",
                channel = "discord",
            },
        }), ct);

        await publisher.Received(1).Publish(
            Arg.Is<SessionMessageReceivedNotification>(n =>
                n.SessionKey == "sess-42"
                && n.Role == "user"
                && n.Content == "hello world"
                && n.Author == "alice"
                && n.Channel == "discord"
                && n.Seq == 7),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SessionMessage_WithArrayContent_IsFlattenedToText()
    {
        var ct = TestContext.Current.CancellationToken;
        var (router, _, publisher) = BuildRouter();

        await router.Handle(Notif("session.message", new
        {
            sessionKey = "sess-43",
            message = new
            {
                role = "assistant",
                content = new object[]
                {
                    new { type = "text", text = "part one " },
                    new { type = "text", text = "part two" },
                },
            },
        }), ct);

        await publisher.Received(1).Publish(
            Arg.Is<SessionMessageReceivedNotification>(n => n.Content == "part one part two"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecApprovalRequested_IsPublishedWithKindExec()
    {
        var ct = TestContext.Current.CancellationToken;
        var (router, _, publisher) = BuildRouter();

        await router.Handle(Notif("exec.approval.requested", new
        {
            id = "appr-1",
            createdAtMs = 1712345678000L,
            expiresAtMs = 1712345978000L,
            request = new
            {
                displayName = "Delete backups",
                description = "Destructive command",
                command = "rm -rf /data",
            },
            turnSourceChannel = "slack",
        }), ct);

        await publisher.Received(1).Publish(
            Arg.Is<ApprovalRequestedNotification>(n =>
                n.Id == "appr-1"
                && n.Kind == "exec"
                && n.Title == "Delete backups"
                && n.Summary == "Destructive command"
                && n.Command == "rm -rf /data"
                && n.SourceChannel == "slack"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PluginApprovalRequested_UsesKindPlugin()
    {
        var ct = TestContext.Current.CancellationToken;
        var (router, _, publisher) = BuildRouter();

        await router.Handle(Notif("plugin.approval.requested", new
        {
            id = "appr-2",
            request = new { title = "Install ext", summary = "Third-party plugin" },
        }), ct);

        await publisher.Received(1).Publish(
            Arg.Is<ApprovalRequestedNotification>(n =>
                n.Kind == "plugin" && n.Title == "Install ext" && n.Summary == "Third-party plugin"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApprovalResolved_IsPublished()
    {
        var ct = TestContext.Current.CancellationToken;
        var (router, _, publisher) = BuildRouter();

        await router.Handle(Notif("exec.approval.resolved", new
        {
            id = "appr-1",
            decision = "allow",
            resolvedBy = "admin",
            resolvedAtMs = 1712346000000L,
        }), ct);

        await publisher.Received(1).Publish(
            Arg.Is<ApprovalResolvedNotification>(n =>
                n.Id == "appr-1"
                && n.Kind == "exec"
                && n.Decision == "allow"
                && n.ResolvedBy == "admin"
                && n.ResolvedAtMs == 1712346000000L),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AgentEvent_IsPublished_WithStreamAndPhase()
    {
        var ct = TestContext.Current.CancellationToken;
        var (router, _, publisher) = BuildRouter();

        await router.Handle(Notif("agent", new
        {
            runId = "run-a",
            sessionKey = "sess-a",
            stream = "tool",
            seq = 12,
            data = new { phase = "start", name = "fetch_data" },
        }, seq: 99), ct);

        await publisher.Received(1).Publish(
            Arg.Is<AgentEventNotification>(n =>
                n.RunId == "run-a"
                && n.SessionKey == "sess-a"
                && n.Stream == "tool"
                && n.Phase == "start"
                && n.Seq == 99),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnknownEvent_IsSilentlyIgnored()
    {
        var ct = TestContext.Current.CancellationToken;
        var (router, _, publisher) = BuildRouter();

        await router.Handle(Notif("something.obscure", new { x = 1 }), ct);

        await publisher.DidNotReceiveWithAnyArgs().Publish<INotification>(default!, ct);
    }

    [Fact]
    public async Task MalformedPayload_IsHandledGracefully()
    {
        var ct = TestContext.Current.CancellationToken;
        var (router, _, publisher) = BuildRouter();

        await router.Handle(Notif("session.message", new { foo = "bar" }), ct);

        await publisher.DidNotReceiveWithAnyArgs().Publish<INotification>(default!, ct);
    }
}
