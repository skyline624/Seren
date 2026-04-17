using Mediator;

namespace Seren.Application.Chat;

/// <summary>
/// Command to send a text message to the active character and stream
/// the LLM response back to connected peers.
/// </summary>
/// <param name="Text">User's text input.</param>
/// <param name="SessionId">Optional session identifier for conversation continuity.</param>
/// <param name="PeerId">Optional peer id of the originating client.</param>
/// <param name="Model">
/// Optional LLM model identifier override. When set, it takes precedence
/// over the active character's <c>AgentId</c> and the gateway's
/// <c>DefaultAgentId</c> fallback. Typically set by the UI Settings panel.
/// </param>
public sealed record SendTextMessageCommand(
    string Text,
    Guid? SessionId = null,
    string? PeerId = null,
    string? Model = null) : ICommand;
