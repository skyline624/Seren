using Seren.Domain.Entities;
using Seren.Domain.ValueObjects;
using Seren.Infrastructure.Realtime;
using Shouldly;
using Xunit;

namespace Seren.Infrastructure.Tests.Realtime;

public sealed class InMemoryPeerRegistryTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 15, 12, 0, 0, TimeSpan.Zero);

    private static Peer NewPeer(PeerId? id = null) =>
        Peer.CreateNew(id ?? PeerId.New(), Now, authRequired: false);

    [Fact]
    public void Add_ShouldInsertPeer()
    {
        // arrange
        var registry = new InMemoryPeerRegistry();
        var peer = NewPeer();

        // act
        var added = registry.Add(peer);

        // assert
        added.ShouldBeTrue();
        registry.Count.ShouldBe(1);
        registry.TryGet(peer.Id, out var stored).ShouldBeTrue();
        stored.ShouldBe(peer);
    }

    [Fact]
    public void Add_ShouldRejectDuplicate()
    {
        // arrange
        var registry = new InMemoryPeerRegistry();
        var peer = NewPeer();
        registry.Add(peer);

        // act
        var duplicate = registry.Add(peer);

        // assert
        duplicate.ShouldBeFalse();
        registry.Count.ShouldBe(1);
    }

    [Fact]
    public void Remove_ShouldEvictPeer()
    {
        // arrange
        var registry = new InMemoryPeerRegistry();
        var peer = NewPeer();
        registry.Add(peer);

        // act
        var removed = registry.Remove(peer.Id);

        // assert
        removed.ShouldBeTrue();
        registry.Count.ShouldBe(0);
        registry.TryGet(peer.Id, out _).ShouldBeFalse();
    }

    [Fact]
    public void Update_ShouldReplaceSnapshot()
    {
        // arrange
        var registry = new InMemoryPeerRegistry();
        var peer = NewPeer();
        registry.Add(peer);

        var identity = new ModuleIdentity("web-01", "stage-web");
        var updated = peer.Announce(identity);

        // act
        var ok = registry.Update(updated);

        // assert
        ok.ShouldBeTrue();
        registry.TryGet(peer.Id, out var stored).ShouldBeTrue();
        stored!.Identity.ShouldBe(identity);
    }

    [Fact]
    public void Update_ShouldReturnFalseForUnknownPeer()
    {
        // arrange
        var registry = new InMemoryPeerRegistry();

        // act
        var ok = registry.Update(NewPeer());

        // assert
        ok.ShouldBeFalse();
    }

    [Fact]
    public void Snapshot_ShouldReturnAllCurrentPeers()
    {
        // arrange
        var registry = new InMemoryPeerRegistry();
        var a = NewPeer();
        var b = NewPeer();
        registry.Add(a);
        registry.Add(b);

        // act
        var snapshot = registry.Snapshot();

        // assert
        snapshot.Count.ShouldBe(2);
        snapshot.ShouldContain(a);
        snapshot.ShouldContain(b);
    }
}
