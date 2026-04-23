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
/// <param name="ClientMessageId">
/// Optional client-minted id for the user turn. Used both as the
/// optimistic-bubble id on the originating tab and — propagated to
/// OpenClaw as <c>idempotencyKey</c> — as the runId for the streaming
/// response. Letting the client choose this id lets the UI target a
/// specific run when the user clicks Stop.
/// </param>
public sealed record SendTextMessageCommand(
    string Text,
    Guid? SessionId = null,
    string? PeerId = null,
    string? Model = null,
    string? ClientMessageId = null) : ICommand;
