using FluentValidation;
using Mediator;

namespace Seren.Application.Characters;

/// <summary>
/// Command to delete a character by its unique identifier.
/// </summary>
public sealed record DeleteCharacterCommand(Guid Id) : ICommand;

/// <summary>
/// Validates <see cref="DeleteCharacterCommand"/>.
/// </summary>
public sealed class DeleteCharacterValidator : AbstractValidator<DeleteCharacterCommand>
{
    public DeleteCharacterValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Character id is required.");
    }
}
