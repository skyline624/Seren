using Mediator;

namespace Seren.Application.Chat;

/// <summary>
/// Command issued when a client clicks Stop while a chat run is streaming.
/// The handler resolves the active runId from <see cref="Abstractions.IChatRunRegistry"/>
/// (when <see cref="RunId"/> is omitted) and asks OpenClaw to abort it.
/// </summary>
/// <param name="RunId">
/// Specific run to abort. Optional — when omitted the handler aborts whatever
/// run is currently active for the shared session. The streaming handler
/// emits <c>OutputChatEnd</c> regardless, so the UI flips back to idle from
/// its existing teardown path; this command does not broadcast anything
/// itself.
/// </param>
public sealed record AbortChatCommand(string? RunId = null) : ICommand;
