using FluentValidation;
using Mediator;

namespace Seren.Application.Characters;

/// <summary>
/// Command to set a character as the active persona. Deactivates all others.
/// </summary>
public sealed record ActivateCharacterCommand(Guid Id) : ICommand;

/// <summary>
/// Validates <see cref="ActivateCharacterCommand"/>.
/// </summary>
public sealed class ActivateCharacterValidator : AbstractValidator<ActivateCharacterCommand>
{
    public ActivateCharacterValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Character id is required.");
    }
}
