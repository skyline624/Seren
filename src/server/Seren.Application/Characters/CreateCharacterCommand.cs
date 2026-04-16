using FluentValidation;
using Mediator;
using Seren.Domain.Entities;

namespace Seren.Application.Characters;

/// <summary>
/// Command to create a new character.
/// </summary>
/// <param name="Name">Display name of the character (1–100 chars).</param>
/// <param name="SystemPrompt">System prompt sent to the LLM (1–4000 chars).</param>
/// <param name="VrmAssetPath">Optional path to the VRM asset file.</param>
/// <param name="Voice">Optional voice identifier in "provider:voiceId" format (e.g. "openai:nova").</param>
/// <param name="AgentId">Optional OpenClaw agent identifier.</param>
public sealed record CreateCharacterCommand(
    string Name,
    string SystemPrompt,
    string? VrmAssetPath,
    string? Voice,
    string? AgentId) : ICommand<Character>;

/// <summary>
/// Validates <see cref="CreateCharacterCommand"/> before the handler runs.
/// </summary>
public sealed class CreateCharacterValidator : AbstractValidator<CreateCharacterCommand>
{
    public CreateCharacterValidator()
    {
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
