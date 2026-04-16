using Seren.Domain.Entities;
using Seren.Domain.ValueObjects;
using Shouldly;
using Xunit;

namespace Seren.Domain.Tests.Entities;

public sealed class PeerTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateNew_WhenAuthNotRequired_ShouldBeImmediatelyAuthenticated()
    {
        // arrange
        var id = PeerId.New();

        // act
        var peer = Peer.CreateNew(id, Now, authRequired: false);

        // assert
        peer.Id.ShouldBe(id);
        peer.IsAuthenticated.ShouldBeTrue();
        peer.Identity.ShouldBeNull();
        peer.ConnectedAt.ShouldBe(Now);
        peer.LastHeartbeatAt.ShouldBe(Now);
        peer.MissedHeartbeats.ShouldBe(0);
    }

    [Fact]
    public void CreateNew_WhenAuthRequired_ShouldBeUnauthenticated()
    {
        var peer = Peer.CreateNew(PeerId.New(), Now, authRequired: true);

        peer.IsAuthenticated.ShouldBeFalse();
    }

    [Fact]
    public void Authenticate_ShouldReturnNewAuthenticatedSnapshot()
    {
        // arrange
        var peer = Peer.CreateNew(PeerId.New(), Now, authRequired: true);

        // act
        var authenticated = peer.Authenticate();

        // assert
        authenticated.IsAuthenticated.ShouldBeTrue();
        peer.IsAuthenticated.ShouldBeFalse(); // original is immutable
        authenticated.Id.ShouldBe(peer.Id);
    }

    [Fact]
    public void Announce_ShouldAttachIdentity()
    {
        // arrange
        var peer = Peer.CreateNew(PeerId.New(), Now, authRequired: false);
        var identity = new ModuleIdentity("telegram-01", "telegram-bot", "0.1.0");

        // act
        var announced = peer.Announce(identity);

        // assert
        announced.Identity.ShouldBe(identity);
        peer.Identity.ShouldBeNull();
    }

    [Fact]
    public void Beat_ShouldRefreshHeartbeatAndResetMissCounter()
    {
        // arrange
        var peer = Peer.CreateNew(PeerId.New(), Now, authRequired: false) with { MissedHeartbeats = 3 };
        var later = Now.AddSeconds(15);

        // act
        var beated = peer.Beat(later);

        // assert
        beated.LastHeartbeatAt.ShouldBe(later);
        beated.MissedHeartbeats.ShouldBe(0);
    }
}
