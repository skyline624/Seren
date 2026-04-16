using Mediator;

namespace Seren.Application.Chat;

/// <summary>
/// Command to send a text message to the active character and stream
/// the LLM response back to connected peers.
/// </summary>
/// <param name="Text">User's text input.</param>
/// <param name="SessionId">Optional session identifier for conversation continuity.</param>
/// <param name="PeerId">Optional peer id of the originating client.</param>
public sealed record SendTextMessageCommand(
    string Text,
    Guid? SessionId = null,
    string? PeerId = null) : ICommand;
