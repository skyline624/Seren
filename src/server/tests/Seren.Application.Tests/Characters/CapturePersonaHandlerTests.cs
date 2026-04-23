using Microsoft.Extensions.Logging.Abstractions;
using Seren.Application.Abstractions;
using Seren.Application.Characters;
using Seren.Application.Characters.Personas;
using Seren.Contracts.Characters;
using Seren.Domain.Entities;
using Shouldly;
using Xunit;

namespace Seren.Application.Tests.Characters;

/// <summary>
/// Handler tests for <see cref="CapturePersonaHandler"/> — every
/// branch of the reader tri-state + ActivateOnCapture flag + metrics
/// contract.
/// </summary>
public sealed class CapturePersonaHandlerTests
{
    [Fact]
    public async Task Handle_LoadedSnapshot_PersistsCharacter_AndRecordsOk()
    {
        var ct = TestContext.Current.CancellationToken;
        var cortana = BuildValidCortanaCharacter();
        var reader = new StubReader(PersonaReadResult.Loaded(new WorkspacePersonaSnapshot(
            PersonaTemplateComposer.ComposeIdentity(cortana),
            PersonaTemplateComposer.ComposeSoul(cortana))));
        var repo = new CapturingRepository();
        var metrics = new CapturingMetrics();

        var handler = new CapturePersonaHandler(reader, repo, metrics, NullLogger<CapturePersonaHandler>.Instance);

        var result = await handler.Handle(new CapturePersonaCommand(), ct);

        result.Character.Name.ShouldBe("Cortana");
        result.Character.SystemPrompt.ShouldBe(cortana.SystemPrompt.Trim());
        repo.Added.Count.ShouldBe(1);
        repo.Activated.ShouldBeNull();
        metrics.Outcomes.ShouldBe(["ok"]);
    }

    [Fact]
    public async Task Handle_ActivateFlag_CallsSetActiveAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var cortana = BuildValidCortanaCharacter();
        var reader = new StubReader(PersonaReadResult.Loaded(new WorkspacePersonaSnapshot(
            PersonaTemplateComposer.ComposeIdentity(cortana),
            PersonaTemplateComposer.ComposeSoul(cortana))));
        var repo = new CapturingRepository();
        var metrics = new CapturingMetrics();

        var handler = new CapturePersonaHandler(reader, repo, metrics, NullLogger<CapturePersonaHandler>.Instance);

        var result = await handler.Handle(new CapturePersonaCommand(ActivateOnCapture: true), ct);

        repo.Activated.ShouldBe(result.Character.Id);
    }

    [Fact]
    public async Task Handle_EmptyWorkspace_ThrowsWorkspaceEmpty_AndRecordsCode()
    {
        var ct = TestContext.Current.CancellationToken;
        var reader = new StubReader(PersonaReadResult.Empty);
        var repo = new CapturingRepository();
        var metrics = new CapturingMetrics();

        var handler = new CapturePersonaHandler(reader, repo, metrics, NullLogger<CapturePersonaHandler>.Instance);

        var ex = await Should.ThrowAsync<PersonaCaptureException>(async () =>
            await handler.Handle(new CapturePersonaCommand(), ct));
        ex.Code.ShouldBe(PersonaCaptureError.WorkspaceEmpty);
        repo.Added.ShouldBeEmpty();
        metrics.Outcomes.ShouldBe([PersonaCaptureError.WorkspaceEmpty]);
    }

    [Fact]
    public async Task Handle_NoWorkspaceConfigured_ThrowsTypedCode_AndRecordsCode()
    {
        var ct = TestContext.Current.CancellationToken;
        var reader = new StubReader(PersonaReadResult.NotConfigured);
        var repo = new CapturingRepository();
        var metrics = new CapturingMetrics();

        var handler = new CapturePersonaHandler(reader, repo, metrics, NullLogger<CapturePersonaHandler>.Instance);

        var ex = await Should.ThrowAsync<PersonaCaptureException>(async () =>
            await handler.Handle(new CapturePersonaCommand(), ct));
        ex.Code.ShouldBe(PersonaCaptureError.NoWorkspaceConfigured);
        metrics.Outcomes.ShouldBe([PersonaCaptureError.NoWorkspaceConfigured]);
    }

    [Fact]
    public async Task Handle_InvalidMarkdown_ThrowsInvalidPersona_AndRecordsCode()
    {
        var ct = TestContext.Current.CancellationToken;
        var reader = new StubReader(PersonaReadResult.Loaded(new WorkspacePersonaSnapshot(
            "no heading anywhere",
            "no heading either")));
        var repo = new CapturingRepository();
        var metrics = new CapturingMetrics();

        var handler = new CapturePersonaHandler(reader, repo, metrics, NullLogger<CapturePersonaHandler>.Instance);

        var ex = await Should.ThrowAsync<PersonaCaptureException>(async () =>
            await handler.Handle(new CapturePersonaCommand(), ct));
        ex.Code.ShouldBe(PersonaCaptureError.InvalidPersona);
        repo.Added.ShouldBeEmpty();
        metrics.Outcomes.ShouldBe([PersonaCaptureError.InvalidPersona]);
    }

    [Fact]
    public async Task Handle_PersistedCharacter_CarriesCaptureProvenanceMetadata()
    {
        var ct = TestContext.Current.CancellationToken;
        var cortana = BuildValidCortanaCharacter();
        var reader = new StubReader(PersonaReadResult.Loaded(new WorkspacePersonaSnapshot(
            PersonaTemplateComposer.ComposeIdentity(cortana),
            PersonaTemplateComposer.ComposeSoul(cortana))));
        var repo = new CapturingRepository();

        var handler = new CapturePersonaHandler(reader, repo, new CapturingMetrics(), NullLogger<CapturePersonaHandler>.Instance);

        await handler.Handle(new CapturePersonaCommand(), ct);

        var saved = repo.Added.ShouldHaveSingleItem();
        saved.ImportMetadataJson.ShouldNotBeNull();
        saved.ImportMetadataJson.ShouldContain("\"source\":\"captured\"");
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static Character BuildValidCortanaCharacter() =>
        Character.Create("Cortana", "You are Cortana, witty and loyal.") with
        {
            Greeting = "Cortana online.",
            Description = "UNSC Smart AI.",
            Tags = ["scifi", "halo"],
        };

    private sealed class StubReader : IPersonaWorkspaceReader
    {
        private readonly PersonaReadResult _result;
        public StubReader(PersonaReadResult result) { _result = result; }

        public Task<PersonaReadResult> ReadCurrentPersonaAsync(CancellationToken ct)
            => Task.FromResult(_result);
    }

    private sealed class CapturingRepository : ICharacterRepository
    {
        public List<Character> Added { get; } = [];
        public Guid? Activated { get; private set; }

        public Task AddAsync(Character character, CancellationToken ct)
        {
            Added.Add(character);
            return Task.CompletedTask;
        }

        public Task SetActiveAsync(Guid id, CancellationToken ct)
        {
            Activated = id;
            return Task.CompletedTask;
        }

        public Task<Character?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult<Character?>(Added.FirstOrDefault(c => c.Id == id));

        public Task<Character?> GetActiveAsync(CancellationToken ct) => Task.FromResult<Character?>(null);

        public Task<IReadOnlyList<Character>> GetAllAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Character>>(Added);

        public Task UpdateAsync(Character character, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> DeleteAsync(Guid id, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class CapturingMetrics : IPersonaCaptureMetrics
    {
        public List<string> Outcomes { get; } = [];
        public void RecordCapture(string outcome) => Outcomes.Add(outcome);
    }
}
