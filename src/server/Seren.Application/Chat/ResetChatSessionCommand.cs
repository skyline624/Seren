using Mediator;

namespace Seren.Application.Chat;

/// <summary>
/// Resets the OpenClaw main session: archives the current transcript and
/// starts a fresh one for the same session key. The LLM context is wiped,
/// but the long-term memory plugins (vector DB, active memory) and the
/// device pairing are unaffected. All connected peers are notified via a
/// broadcast <c>output:chat:cleared</c> event so their local message lists
/// clear in lockstep.
/// </summary>
public sealed record ResetChatSessionCommand : ICommand;
