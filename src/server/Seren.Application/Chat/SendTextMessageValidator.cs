using FluentValidation;

namespace Seren.Application.Chat;

/// <summary>
/// Validates <see cref="SendTextMessageCommand"/> before the handler runs.
/// </summary>
public sealed class SendTextMessageValidator : AbstractValidator<SendTextMessageCommand>
{
    public SendTextMessageValidator()
    {
        RuleFor(x => x.Text)
            .NotEmpty()
            .WithMessage("Text must not be empty.")
            .MaximumLength(4000)
            .WithMessage("Text must not exceed 4000 characters.");
    }
}
