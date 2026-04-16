using FluentValidation;

namespace Seren.Application.Audio;

/// <summary>
/// Validates <see cref="SubmitVoiceInputCommand"/> before the handler runs.
/// </summary>
public sealed class SubmitVoiceInputValidator : AbstractValidator<SubmitVoiceInputCommand>
{
    public SubmitVoiceInputValidator()
    {
        RuleFor(x => x.AudioData)
            .NotEmpty()
            .WithMessage("Audio data must not be empty.");

        RuleFor(x => x.Format)
            .NotEmpty()
            .WithMessage("Audio format must not be empty.")
            .MaximumLength(10)
            .WithMessage("Audio format must not exceed 10 characters.");
    }
}
