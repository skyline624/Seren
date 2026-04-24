using FluentValidation;
using Mediator;
using Seren.Domain.Entities;

namespace Seren.Application.Characters;

/// <summary>
/// Command to update an existing character's properties.
/// </summary>
public sealed record UpdateCharacterCommand(
    Guid Id,
    string Name,
    string SystemPrompt,
    string? AvatarModelPath,
    string? Voice,
    string? AgentId) : ICommand<Character>;

/// <summary>
/// Validates <see cref="UpdateCharacterCommand"/> before the handler runs.
/// </summary>
public sealed class UpdateCharacterValidator : AbstractValidator<UpdateCharacterCommand>
{
    public UpdateCharacterValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Character id is required.");

        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Character name is required.")
            .MaximumLength(100)
            .WithMessage("Character name must not exceed 100 characters.");

        RuleFor(x => x.SystemPrompt)
            .NotEmpty()
            .WithMessage("System prompt is required.")
            .MaximumLength(4000)
            .WithMessage("System prompt must not exceed 4000 characters.");
    }
}
