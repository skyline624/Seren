using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Seren.Application.Chat;
using Seren.Application.Sessions;
using Seren.Domain.ValueObjects;
using Shouldly;
using Xunit;

namespace Seren.Application.Tests.Sessions;

public sealed class HydrateHistoryOnAnnouncedHandlerTests
{
    [Fact]
    public async Task Handle_DispatchesLoadChatHistoryCommand_WithInitialHydrationLimit()
    {
        var ct = TestContext.Current.CancellationToken;
        var sender = Substitute.For<ISender>();
        var handler = new HydrateHistoryOnAnnouncedHandler(
            sender, NullLogger<HydrateHistoryOnAnnouncedHandler>.Instance);

        var peerId = PeerId.New();
        await handler.Handle(new PeerAnnouncedNotification(peerId, "stage-web-01", "stage-web"), ct);

        await sender.Received(1).Send(
            Arg.Is<LoadChatHistoryCommand>(c =>
                c.TargetPeer == peerId
                && c.Before == null
                && c.Limit == HydrateHistoryOnAnnouncedHandler.InitialHydrationLimit),
            Arg.Any<CancellationToken>());
    }
}
