using System.Diagnostics;
using Mediator;
using Microsoft.Extensions.Logging;
using Seren.Application.Abstractions;
using Seren.Application.Characters.Import;
using Seren.Contracts.Characters;
using Seren.Domain.Entities;

namespace Seren.Application.Characters;

/// <summary>
/// Orchestration for <see cref="ImportCharacterCardCommand"/> — pure
/// composition over abstractions (DIP) : parser, avatar store,
/// repository, metrics. No file I/O, no HTTP, no JSON parsing happens
/// here ; each responsibility lives behind its own interface.
/// </summary>
/// <remarks>
/// Flow :
/// <list type="number">
/// <item><description><see cref="ICharacterCardParser.Parse"/> → throws
/// <see cref="CharacterImportException"/> on invalid input; handler
/// records the outcome metric then rethrows so the endpoint can map it
/// to a typed 4xx response.</description></item>
/// <item><description><see cref="Character.CreateFromCard"/> — the
/// card → domain mapping lives in the domain itself; handler just
/// passes primitives.</description></item>
/// <item><description>If the card carried a PNG, persist it via
/// <see cref="ICharacterAvatarStore.SaveAsync"/>; stamp the returned
/// relative path on the <see cref="Character"/>.</description></item>
/// <item><description>Persist via <see cref="ICharacterRepository.AddAsync"/>.</description></item>
/// <item><description>If <see cref="ImportCharacterCardCommand.ActivateOnImport"/>,
/// flip the active character.</description></item>
/// <item><description>Record the success outcome with tagged metrics.</description></item>
/// </list>
/// </remarks>
public sealed class ImportCharacterCardHandler
    : ICommandHandler<ImportCharacterCardCommand, ImportedCharacterResult>
{
    private readonly ICharacterCardParser _parser;
    private readonly ICharacterAvatarStore _avatarStore;
    private readonly ICharacterRepository _repository;
    private readonly ICharacterImportMetrics _metrics;
    private readonly ILogger<ImportCharacterCardHandler> _logger;

    public ImportCharacterCardHandler(
        ICharacterCardParser parser,
        ICharacterAvatarStore avatarStore,
        ICharacterRepository repository,
        ICharacterImportMetrics metrics,
        ILogger<ImportCharacterCardHandler> logger)
    {
        _parser = parser;
        _avatarStore = avatarStore;
        _repository = repository;
        _metrics = metrics;
        _logger = logger;
    }

    public async ValueTask<ImportedCharacterResult> Handle(
        ImportCharacterCardCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var stopwatch = Stopwatch.StartNew();
        var specVersion = "unknown";
        var hadAvatar = false;

        try
        {
            var data = _parser.Parse(command.FileBytes, command.FileName);
            specVersion = data.SpecVersion;
            hadAvatar = data.AvatarPng is not null;

            var characterId = Guid.NewGuid();
            string? avatarPath = null;
            if (data.AvatarPng is not null)
            {
                avatarPath = await _avatarStore
                    .SaveAsync(characterId, data.AvatarPng, cancellationToken)
                    .ConfigureAwait(false);
            }

            var character = Character.CreateFromCard(
                name: data.Name,
                systemPrompt: data.SystemPrompt,
                greeting: data.Greeting,
                description: data.Description,
                tags: data.Tags,
                avatarImagePath: avatarPath,
                importMetadataJson: data.ImportMetadataJson) with
            {
                Id = characterId,   // honour the id already used for the avatar filename
            };

            await _repository.AddAsync(character, cancellationToken).ConfigureAwait(false);
            if (command.ActivateOnImport)
            {
                await _repository.SetActiveAsync(character.Id, cancellationToken).ConfigureAwait(false);
                character = character with { IsActive = true };
            }

            stopwatch.Stop();
            _metrics.RecordImport(outcome: "ok", specVersion, hadAvatar, stopwatch.Elapsed);
            _logger.LogInformation(
                "Imported character {CharacterId} '{Name}' from {FileName} (spec={Spec}, hadAvatar={HadAvatar}, warnings={Warnings})",
                character.Id, character.Name, command.FileName, specVersion, hadAvatar,
                string.Join(",", data.Warnings));

            return new ImportedCharacterResult(character, data.Warnings);
        }
        catch (CharacterImportException ex)
        {
            stopwatch.Stop();
            _metrics.RecordImport(outcome: ex.Code, specVersion, hadAvatar, stopwatch.Elapsed);
            _logger.LogWarning(
                "Character card import rejected: {Code} — {Message} (file={FileName}, details={Details})",
                ex.Code, ex.Message, command.FileName, ex.Details);
            throw;
        }
    }
}
