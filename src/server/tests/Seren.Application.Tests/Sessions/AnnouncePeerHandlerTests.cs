using Microsoft.Extensions.Logging.Abstractions;
using Seren.Application.Sessions;
using Seren.Contracts.Events;
using Seren.Contracts.Events.Payloads;
using Seren.Domain.Entities;
using Seren.Domain.ValueObjects;
using Shouldly;
using Xunit;

namespace Seren.Application.Tests.Sessions;

public sealed class AnnouncePeerHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_WhenPeerExists_ShouldAttachIdentityAndReturnAnnouncedPayload()
    {
        // arrange
        var peerId = PeerId.New();
        var registry = new FakePeerRegistry();
        registry.Add(Peer.CreateNew(peerId, Now, authRequired: false));

        var handler = new AnnouncePeerHandler(registry, NullLogger<AnnouncePeerHandler>.Instance);

        var command = new AnnouncePeerCommand(
            PeerId: peerId,
            Payload: new AnnouncePayload
            {
                Identity = new ModuleIdentityDto { Id = "stage-web-01", PluginId = "stage-web" },
                Name = "Seren Web",
            },
            ParentEventId: "evt-123");

        // act
        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        // assert
        result.Identity.Id.ShouldBe("stage-web-01");
        result.Identity.PluginId.ShouldBe("stage-web");
        result.Name.ShouldBe("Seren Web");
        result.Index.ShouldBe(0);

        registry.UpdateCalls.ShouldBe(1);
        registry.TryGet(peerId, out var stored).ShouldBeTrue();
        stored!.Identity.ShouldNotBeNull();
        stored.Identity!.Id.ShouldBe("stage-web-01");
        stored.Identity.PluginId.ShouldBe("stage-web");
    }

    [Fact]
    public async Task Handle_WhenPeerDoesNotExist_ShouldThrowInvalidOperation()
    {
        // arrange
        var registry = new FakePeerRegistry();
        var handler = new AnnouncePeerHandler(registry, NullLogger<AnnouncePeerHandler>.Instance);

        var command = new AnnouncePeerCommand(
            PeerId.New(),
            new AnnouncePayload
            {
                Identity = new ModuleIdentityDto { Id = "ghost", PluginId = "ghost" },
                Name = "Ghost",
            },
            "evt-xyz");

        // act + assert
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await handler.Handle(command, TestContext.Current.CancellationToken));
    }
}
