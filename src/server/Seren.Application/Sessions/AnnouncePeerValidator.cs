using FluentValidation;

namespace Seren.Application.Sessions;

/// <summary>
/// Validates <see cref="AnnouncePeerCommand"/> before the handler runs.
/// </summary>
public sealed class AnnouncePeerValidator : AbstractValidator<AnnouncePeerCommand>
{
    public AnnouncePeerValidator()
    {
        RuleFor(x => x.ParentEventId)
            .NotEmpty()
            .WithMessage("ParentEventId is required for causal tracing.");

        RuleFor(x => x.Payload)
            .NotNull()
            .WithMessage("Announce payload is required.");

        RuleFor(x => x.Payload.Name)
            .NotEmpty()
            .WithMessage("Announce payload must contain a non-empty module name.")
            .When(x => x.Payload is not null);

        RuleFor(x => x.Payload.Identity)
            .NotNull()
            .WithMessage("Announce payload must contain an identity block.")
            .When(x => x.Payload is not null);

        RuleFor(x => x.Payload.Identity.Id)
            .NotEmpty()
            .WithMessage("Identity.Id must be non-empty.")
            .When(x => x.Payload?.Identity is not null);

        RuleFor(x => x.Payload.Identity.PluginId)
            .NotEmpty()
            .WithMessage("Identity.PluginId must be non-empty.")
            .When(x => x.Payload?.Identity is not null);
    }
}
