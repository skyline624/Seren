using Microsoft.Extensions.Logging.Abstractions;
using Seren.Application.Abstractions;
using Seren.Application.Characters;
using Seren.Application.Characters.Import;
using Seren.Contracts.Characters;
using Seren.Domain.Entities;
using Shouldly;
using Xunit;

namespace Seren.Application.Tests.Characters;

/// <summary>
/// Orchestration tests for <see cref="ImportCharacterCardHandler"/>.
/// Parser + store + repo are stubbed so each assertion targets exactly
/// one orchestration concern (metrics tagging, avatar persistence,
/// activation flag, error pass-through).
/// </summary>
public sealed class ImportCharacterCardHandlerTests
{
    [Fact]
    public async Task Handle_JsonCard_PersistsCharacter_AndRecordsOkOutcome()
    {
        var ct = TestContext.Current.CancellationToken;
        var parser = new StubParser(new CharacterCardData(
            SpecVersion: "chara_card_v3",
            Name: "Cortana",
            SystemPrompt: "prompt",
            Greeting: "Hello.",
            Description: "AI.",
            Tags: [],
            Creator: null,
            CharacterVersion: null,
            AvatarPng: null,
            ImportMetadataJson: "{}",
            Warnings: []));
        var repo = new StubRepository();
        var store = new StubAvatarStore();
        var metrics = new StubMetrics();

        var handler = new ImportCharacterCardHandler(
            parser, store, repo, metrics, NullLogger<ImportCharacterCardHandler>.Instance);

        var result = await handler.Handle(
            new ImportCharacterCardCommand([1, 2, 3], "cortana.json"), ct);

        result.Character.Name.ShouldBe("Cortana");
        result.Character.AvatarImagePath.ShouldBeNull();
        repo.Added.Count.ShouldBe(1);
        store.Saved.ShouldBeEmpty("no PNG → avatar store untouched");
        metrics.Records.Count.ShouldBe(1);
        metrics.Records[0].Outcome.ShouldBe("ok");
        metrics.Records[0].SpecVersion.ShouldBe("chara_card_v3");
        metrics.Records[0].HadAvatar.ShouldBeFalse();
    }

    [Fact]
    public async Task Handle_PngCard_PersistsAvatar_AndStampsPath()
    {
        var ct = TestContext.Current.CancellationToken;
        var avatar = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var parser = new StubParser(new CharacterCardData(
            "chara_card_v3", "Chell", "prompt", null, null, [], null, null, avatar, "{}", []));
        var repo = new StubRepository();
        var store = new StubAvatarStore();
        var metrics = new StubMetrics();

        var handler = new ImportCharacterCardHandler(
            parser, store, repo, metrics, NullLogger<ImportCharacterCardHandler>.Instance);

        var result = await handler.Handle(
            new ImportCharacterCardCommand(avatar, "chell.png"), ct);

        store.Saved.Count.ShouldBe(1);
        store.Saved[0].Id.ShouldBe(result.Character.Id);
        store.Saved[0].Bytes.ShouldBe(avatar);
        result.Character.AvatarImagePath.ShouldNotBeNullOrEmpty();
        metrics.Records[0].HadAvatar.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_ActivateOnImport_FlipsActiveFlag_AndCallsRepoSetActive()
    {
        var ct = TestContext.Current.CancellationToken;
        var parser = new StubParser(new CharacterCardData(
            "chara_card_v3", "Cortana", "prompt", null, null, [], null, null, null, "{}", []));
        var repo = new StubRepository();
        var store = new StubAvatarStore();
        var metrics = new StubMetrics();

        var handler = new ImportCharacterCardHandler(
            parser, store, repo, metrics, NullLogger<ImportCharacterCardHandler>.Instance);

        var result = await handler.Handle(
            new ImportCharacterCardCommand([1], "cortana.json", ActivateOnImport: true), ct);

        repo.Activated.Count.ShouldBe(1);
        repo.Activated[0].ShouldBe(result.Character.Id);
        result.Character.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_ParserThrows_MetricsRecordErrorCode_AndRethrows()
    {
        var ct = TestContext.Current.CancellationToken;
        var parser = new StubParser(
            new CharacterImportException(CharacterImportError.UnsupportedSpec, "bad spec"));
        var repo = new StubRepository();
        var store = new StubAvatarStore();
        var metrics = new StubMetrics();

        var handler = new ImportCharacterCardHandler(
            parser, store, repo, metrics, NullLogger<ImportCharacterCardHandler>.Instance);

        var ex = await Should.ThrowAsync<CharacterImportException>(async () =>
            await handler.Handle(new ImportCharacterCardCommand([1], "bad.json"), ct));

        ex.Code.ShouldBe(CharacterImportError.UnsupportedSpec);
        repo.Added.ShouldBeEmpty();
        metrics.Records.Count.ShouldBe(1);
        metrics.Records[0].Outcome.ShouldBe(CharacterImportError.UnsupportedSpec);
    }

    // ── Stubs ─────────────────────────────────────────────────────────

    private sealed class StubParser : ICharacterCardParser
    {
        private readonly CharacterCardData? _data;
        private readonly CharacterImportException? _throw;

        public StubParser(CharacterCardData data) { _data = data; }
        public StubParser(CharacterImportException ex) { _throw = ex; }

        public CharacterCardData Parse(ReadOnlyMemory<byte> bytes, string fileName)
            => _throw is not null ? throw _throw : _data!;
    }

    private sealed class StubRepository : ICharacterRepository
    {
        public List<Character> Added { get; } = [];
        public List<Guid> Activated { get; } = [];

        public Task AddAsync(Character character, CancellationToken cancellationToken)
        {
            Added.Add(character);
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task<Character?> GetActiveAsync(CancellationToken cancellationToken)
            => Task.FromResult<Character?>(null);

        public Task<IReadOnlyList<Character>> GetAllAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Character>>(Added.AsReadOnly());

        public Task<Character?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
            => Task.FromResult(Added.FirstOrDefault(c => c.Id == id));

        public Task SetActiveAsync(Guid id, CancellationToken cancellationToken)
        {
            Activated.Add(id);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Character character, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class StubAvatarStore : ICharacterAvatarStore
    {
        public List<(Guid Id, byte[] Bytes)> Saved { get; } = [];

        public Task<string> SaveAsync(Guid characterId, byte[] pngBytes, CancellationToken cancellationToken)
        {
            Saved.Add((characterId, pngBytes));
            return Task.FromResult($"avatars/{characterId:N}.png");
        }

        public Task<Stream?> OpenReadAsync(Guid characterId, CancellationToken cancellationToken)
            => Task.FromResult<Stream?>(null);

        public Task DeleteAsync(Guid characterId, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class StubMetrics : ICharacterImportMetrics
    {
        public List<(string Outcome, string SpecVersion, bool HadAvatar, TimeSpan Elapsed)> Records { get; } = [];

        public void RecordImport(string outcome, string specVersion, bool hadAvatar, TimeSpan elapsed)
            => Records.Add((outcome, specVersion, hadAvatar, elapsed));
    }
}
