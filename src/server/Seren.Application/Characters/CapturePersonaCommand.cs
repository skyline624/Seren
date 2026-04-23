using Mediator;
using Seren.Domain.Entities;

namespace Seren.Application.Characters;

/// <summary>
/// Command: read OpenClaw's current workspace persona
/// (<c>IDENTITY.md</c> + <c>SOUL.md</c>) and turn it into a new
/// <see cref="Character"/> in Seren's library. Inverse of the
/// persona-writer flow — users run it after OpenClaw's onboarding to
/// promote the custom persona to a first-class, reactivatable
/// <c>Character</c> card.
/// </summary>
/// <param name="ActivateOnCapture">When true, the captured character
/// is also flipped to active immediately — causing the writer to
/// re-emit the same files with the Seren-managed header. Useful when
/// the user wants the capture to "take ownership" of the current
/// persona without an extra click.</param>
public sealed record CapturePersonaCommand(bool ActivateOnCapture = false)
    : ICommand<CapturedPersonaResult>;

/// <summary>Result of a successful <see cref="CapturePersonaCommand"/>.</summary>
/// <param name="Character">Newly-persisted character.</param>
public sealed record CapturedPersonaResult(Character Character);
