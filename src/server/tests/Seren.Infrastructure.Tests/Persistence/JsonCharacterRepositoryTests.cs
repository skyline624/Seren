using Microsoft.Extensions.Options;
using Seren.Domain.Entities;
using Seren.Infrastructure.Persistence.Json;
using Shouldly;
using Xunit;

namespace Seren.Infrastructure.Tests.Persistence;

public sealed class JsonCharacterRepositoryTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"seren_chars_{Guid.NewGuid():N}.json");

    private JsonCharacterRepository NewRepo() =>
        new(Options.Create(new CharacterStoreOptions { StorePath = _path }));

    public void Dispose()
    {
        try { File.Delete(_path); }
        catch { /* best effort */ }
    }

    [Fact]
    public async Task GetAllAsync_OnMissingFile_ReturnsEmpty()
    {
        using var repo = NewRepo();
        var all = await repo.GetAllAsync(TestContext.Current.CancellationToken);
        all.ShouldBeEmpty();
    }

    [Fact]
    public async Task AddAsync_ThenGetById_RoundTrips()
    {
        using var repo = NewRepo();
        var ct = TestContext.Current.CancellationToken;
        var character = Character.Create("Seren", "You are helpful.");

        await repo.AddAsync(character, ct);
        var fetched = await repo.GetByIdAsync(character.Id, ct);

        fetched.ShouldNotBeNull();
        fetched.Name.ShouldBe("Seren");
        fetched.SystemPrompt.ShouldBe("You are helpful.");
    }

    [Fact]
    public async Task AddAsync_PersistsToFile()
    {
        using (var repo = NewRepo())
        {
            await repo.AddAsync(
                Character.Create("Seren", "Hi."),
                TestContext.Current.CancellationToken);
        }

        File.Exists(_path).ShouldBeTrue();
        var json = await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken);
        json.ShouldContain("Seren");

        // A fresh instance reads from disk.
        using var second = NewRepo();
        var all = await second.GetAllAsync(TestContext.Current.CancellationToken);
        all.Count.ShouldBe(1);
    }

    [Fact]
    public async Task UpdateAsync_OverwritesExisting()
    {
        using var repo = NewRepo();
        var ct = TestContext.Current.CancellationToken;
        var character = Character.Create("Seren", "v1");
        await repo.AddAsync(character, ct);

        var updated = character with { SystemPrompt = "v2", UpdatedAt = DateTimeOffset.UtcNow };
        await repo.UpdateAsync(updated, ct);

        var fetched = await repo.GetByIdAsync(character.Id, ct);
        fetched!.SystemPrompt.ShouldBe("v2");
    }

    [Fact]
    public async Task UpdateAsync_OnUnknownId_Throws()
    {
        using var repo = NewRepo();
        var ghost = Character.Create("Ghost", "…");
        await Should.ThrowAsync<InvalidOperationException>(() =>
            repo.UpdateAsync(ghost, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteAsync_ReturnsTrueWhenExists_FalseOtherwise()
    {
        using var repo = NewRepo();
        var ct = TestContext.Current.CancellationToken;
        var character = Character.Create("Seren", "Hi.");
        await repo.AddAsync(character, ct);

        (await repo.DeleteAsync(character.Id, ct)).ShouldBeTrue();
        (await repo.DeleteAsync(character.Id, ct)).ShouldBeFalse();
        (await repo.GetAllAsync(ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task SetActiveAsync_FlipsExactlyOneCharacter()
    {
        using var repo = NewRepo();
        var ct = TestContext.Current.CancellationToken;
        var a = Character.Create("Alice", "…");
        var b = Character.Create("Bob",   "…");
        var c = Character.Create("Carol", "…");
        await repo.AddAsync(a, ct);
        await repo.AddAsync(b, ct);
        await repo.AddAsync(c, ct);

        await repo.SetActiveAsync(b.Id, ct);

        var active = await repo.GetActiveAsync(ct);
        active.ShouldNotBeNull();
        active.Id.ShouldBe(b.Id);

        var all = await repo.GetAllAsync(ct);
        all.Count(x => x.IsActive).ShouldBe(1);

        // Switching to a different active flips off the previous one.
        await repo.SetActiveAsync(c.Id, ct);
        (await repo.GetActiveAsync(ct))!.Id.ShouldBe(c.Id);
        (await repo.GetAllAsync(ct)).Count(x => x.IsActive).ShouldBe(1);
    }

    [Fact]
    public async Task SetActiveAsync_OnUnknownId_Throws()
    {
        using var repo = NewRepo();
        await Should.ThrowAsync<InvalidOperationException>(() =>
            repo.SetActiveAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAllAsync_IsOrderedByName()
    {
        using var repo = NewRepo();
        var ct = TestContext.Current.CancellationToken;
        await repo.AddAsync(Character.Create("Charlie", "…"), ct);
        await repo.AddAsync(Character.Create("Alice",   "…"), ct);
        await repo.AddAsync(Character.Create("Bob",     "…"), ct);

        var all = await repo.GetAllAsync(ct);

        all.Select(c => c.Name).ShouldBe(["Alice", "Bob", "Charlie"]);
    }

    [Fact]
    public async Task ParallelAdd_DoesNotCorruptFile()
    {
        using var repo = NewRepo();
        var ct = TestContext.Current.CancellationToken;
        var tasks = Enumerable.Range(0, 10)
            .Select(i => repo.AddAsync(Character.Create($"C{i:D2}", "…"), ct))
            .ToArray();

        await Task.WhenAll(tasks);

        var all = await repo.GetAllAsync(ct);
        all.Count.ShouldBe(10);

        // Fresh instance proves the file on disk is valid JSON with all 10.
        using var fresh = NewRepo();
        (await fresh.GetAllAsync(ct)).Count.ShouldBe(10);
    }
}
