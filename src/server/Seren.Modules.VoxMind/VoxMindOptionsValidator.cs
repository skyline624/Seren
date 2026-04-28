using FluentValidation;

namespace Seren.Modules.VoxMind;

/// <summary>
/// FluentValidation validator for <see cref="VoxMindOptions"/>.
/// </summary>
public sealed class VoxMindOptionsValidator : AbstractValidator<VoxMindOptions>
{
    public VoxMindOptionsValidator()
    {
        RuleFor(x => x.DefaultLanguage)
            .NotEmpty()
            .Length(2)
            .WithMessage("DefaultLanguage must be a 2-letter ISO 639-1 code.");

        RuleFor(x => x.Tts.FlowMatchingSteps)
            .GreaterThan(0)
            .WithMessage("Tts.FlowMatchingSteps must be > 0.");

        RuleFor(x => x.Tts.CacheCapacity)
            .GreaterThan(0)
            .WithMessage("Tts.CacheCapacity must be > 0.");

        RuleFor(x => x.Stt.MaxChunkSeconds)
            .GreaterThan(0)
            .WithMessage("Stt.MaxChunkSeconds must be > 0.");
    }
}
