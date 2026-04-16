using Seren.Infrastructure.Authentication;
using Shouldly;
using Xunit;

namespace Seren.Infrastructure.Tests.Authentication;

/// <summary>
/// Unit tests for <see cref="InMemoryTokenRevocationStore"/>. Covers add,
/// lookup, natural expiration, and the sweeper's prune pass.
/// </summary>
public sealed class InMemoryTokenRevocationStoreTests
{
    [Fact]
    public async Task IsRevokedAsync_WhenJtiNotStored_ShouldReturnFalse()
    {
        // arrange
        var store = new InMemoryTokenRevocationStore();

        // act
        var revoked = await store.IsRevokedAsync("nonexistent", TestContext.Current.CancellationToken);

        // assert
        revoked.ShouldBeFalse();
    }

    [Fact]
    public async Task RevokeAsync_ThenIsRevokedAsync_ShouldReturnTrue()
    {
        // arrange
        var store = new InMemoryTokenRevocationStore();
        var expires = DateTimeOffset.UtcNow.AddMinutes(5);

        // act
        await store.RevokeAsync("jti-123", expires, TestContext.Current.CancellationToken);
        var revoked = await store.IsRevokedAsync("jti-123", TestContext.Current.CancellationToken);

        // assert
        revoked.ShouldBeTrue();
    }

    [Fact]
    public async Task IsRevokedAsync_WhenExpirationPassed_ShouldReturnFalseAndPrune()
    {
        // arrange
        var store = new InMemoryTokenRevocationStore();
        var expiresInPast = DateTimeOffset.UtcNow.AddMinutes(-1);
        await store.RevokeAsync("jti-expired", expiresInPast, TestContext.Current.CancellationToken);

        // act
        var revoked = await store.IsRevokedAsync("jti-expired", TestContext.Current.CancellationToken);

        // assert
        revoked.ShouldBeFalse();
    }

    [Fact]
    public async Task PruneExpired_ShouldRemoveOnlyExpiredEntries()
    {
        // arrange
        var store = new InMemoryTokenRevocationStore();
        var now = DateTimeOffset.UtcNow;
        await store.RevokeAsync("still-valid", now.AddMinutes(10), TestContext.Current.CancellationToken);
        await store.RevokeAsync("expired-1", now.AddMinutes(-1), TestContext.Current.CancellationToken);
        await store.RevokeAsync("expired-2", now.AddMinutes(-5), TestContext.Current.CancellationToken);

        // act
        var pruned = store.PruneExpired(now);

        // assert
        pruned.ShouldBe(2);
        store.Count.ShouldBe(1);
        (await store.IsRevokedAsync("still-valid", TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    [Fact]
    public async Task RevokeAsync_WithEmptyJti_ShouldThrow()
    {
        // arrange
        var store = new InMemoryTokenRevocationStore();

        // act + assert
        await Should.ThrowAsync<ArgumentException>(
            async () => await store.RevokeAsync(
                string.Empty,
                DateTimeOffset.UtcNow.AddMinutes(1),
                TestContext.Current.CancellationToken));
    }
}
