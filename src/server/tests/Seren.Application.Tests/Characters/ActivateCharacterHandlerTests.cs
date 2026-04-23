using Microsoft.Extensions.Logging.Abstractions;
using Seren.Application.Abstractions;
using Seren.Application.Characters;
using Seren.Domain.Entities;
using Shouldly;
using Xunit;

namespace Seren.Application.Tests.Characters;

/// <summary>
/// Handler tests focused on the persona-writer integration contract :
/// the writer is invoked with the right character on success, and its
/// failure <b>never</b> breaks the activation flow.
/// </summary>
public sealed class ActivateCharacterHandlerTests
{
    [Fact]
    public async Task Handle_ActivatesCharacter_AndInvokesPersonaWriterOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        var character = Character.Create("Cortana", "You are Cortana.");
        var repo = new StubCharacterRepository(character);
        var writer = new CapturingPersonaWriter();

        var handler = new ActivateCharacterHandler(
            repo, writer, NullLogger<ActivateCharacterHandler>.Instance);

        await handler.Handle(new ActivateCharacterCommand(character.Id), ct);

        repo.Activated.ShouldBe(character.Id);
        writer.Writes.Count.ShouldBe(1);
        writer.Writes[0].Name.ShouldBe("Cortana");
    }

    [Fact]
    public async Task Handle_UnknownCharacter_Throws_WithoutCallingWriter()
    {
        var ct = TestContext.Current.CancellationToken;
        var repo = new StubCharacterRepository(null);
        var writer = new CapturingPersonaWriter();

        var handler = new ActivateCharacterHandler(
            repo, writer, NullLogger<ActivateCharacterHandler>.Instance);

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await handler.Handle(new ActivateCharacterCommand(Guid.NewGuid()), ct));

        writer.Writes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WriterThrows_ActivationStillSucceeds()
    {
        var ct = TestContext.Current.CancellationToken;
        var character = Character.Create("GLaDOS", "You are GLaDOS.");
        var repo = new StubCharacterRepository(character);
        var writer = new ThrowingPersonaWriter();

        var handler = new ActivateCharacterHandler(
            repo, writer, NullLogger<ActivateCharacterHandler>.Instance);

        // Best-effort contract: activation must not bubble the writer error.
        await handler.Handle(new ActivateCharacterCommand(character.Id), ct);

        repo.Activated.ShouldBe(character.Id);
    }

    // ── Stubs ─────────────────────────────────────────────────────────

    private sealed class StubCharacterRepository : ICharacterRepository
    {
        private readonly Character? _found;
        public Guid? Activated { get; private set; }

        public StubCharacterRepository(Character? found) { _found = found; }

        public Task<Character?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(_found?.Id == id ? _found : null);

        public Task SetActiveAsync(Guid id, CancellationToken ct)
        {
            Activated = id;
            return Task.CompletedTask;
        }

        public Task<Character?> GetActiveAsync(CancellationToken ct)
            => Task.FromResult<Character?>(_found);

        public Task<IReadOnlyList<Character>> GetAllAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Character>>(_found is null ? [] : [_found]);

        public Task AddAsync(Character character, CancellationToken ct) => Task.CompletedTask;
        public Task UpdateAsync(Character character, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> DeleteAsync(Guid id, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class CapturingPersonaWriter : IPersonaWorkspaceWriter
    {
        public List<Character> Writes { get; } = [];

        public Task WritePersonaAsync(Character character, CancellationToken cancellationToken)
        {
            Writes.Add(character);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingPersonaWriter : IPersonaWorkspaceWriter
    {
        public Task WritePersonaAsync(Character character, CancellationToken cancellationToken)
            => throw new IOException("simulated disk failure");
    }
}
