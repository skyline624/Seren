namespace Seren.Contracts.Events;

/// <summary>
/// Constants for the Seren WebSocket protocol event type names.
/// Names follow the pattern <c>domain:subdomain[:verb]</c> and mirror the
/// AIRI <c>plugin-protocol</c> naming convention.
/// </summary>
public static class EventTypes
{
    // Transport
    public const string TransportHello = "transport:hello";
    public const string TransportHeartbeat = "transport:connection:heartbeat";

    // Module lifecycle
    public const string ModuleAuthenticate = "module:authenticate";
    public const string ModuleAuthenticated = "module:authenticated";
    public const string ModuleAnnounce = "module:announce";
    public const string ModuleAnnounced = "module:announced";
    public const string ModuleDeAnnounced = "module:de-announced";

    // Registry
    public const string RegistryModulesSync = "registry:modules:sync";

    // Chat output
    public const string OutputChatChunk = "output:chat:chunk";
    public const string OutputChatEnd = "output:chat:end";
    public const string OutputChatThinkingStart = "output:chat:thinking:start";
    public const string OutputChatThinkingEnd = "output:chat:thinking:end";

    // User-turn echo — broadcast to every peer on the session except the
    // sender, so multi-tab / multi-device clients stay in sync on the
    // question before the assistant stream starts replying.
    public const string OutputChatUser = "output:chat:user";

    // Chat history hydration (server → single peer)
    public const string OutputChatHistoryBegin = "output:chat:history:begin";
    public const string OutputChatHistoryItem = "output:chat:history:item";
    public const string OutputChatHistoryEnd = "output:chat:history:end";

    // Chat session reset confirmation (server → all peers, broadcast)
    public const string OutputChatCleared = "output:chat:cleared";

    // Informational: the pipeline had to retry or fall back to another
    // provider. Non-terminal — a regular `output:chat:end` still closes
    // the stream. UI shows a transient banner ("Switching to ...") so the
    // user understands why the reply may come from a different model.
    public const string OutputChatProviderDegraded = "output:chat:provider-degraded";

    // Text input
    public const string InputText = "input:text";

    // Chat history pagination request + manual session reset (client → server)
    public const string InputChatHistoryRequest = "input:chat:history:request";
    public const string InputChatReset = "input:chat:reset";

    // User-initiated stream cancellation. The client sends this while
    // isStreaming to tell the hub to stop the current run upstream
    // (OpenClaw chat.abort). Broadcasts `Error`/`OutputChatEnd` are
    // already emitted by the streaming handler on teardown, so no
    // dedicated "aborted" event is needed.
    public const string InputChatAbort = "input:chat:abort";

    // Voice input
    public const string InputVoice = "input:voice";

    // Audio output
    public const string AudioPlaybackChunk = "audio:playback:chunk";
    public const string AudioLipsyncFrame = "audio:lipsync:frame";

    // Avatar
    public const string AvatarEmotion = "avatar:emotion";
    public const string AvatarAction = "avatar:action";

    // Session / channel messages arriving via OpenClaw (Discord/Slack/Telegram/…)
    public const string OutputSessionMessage = "output:session:message";

    // Approval flows surfaced from OpenClaw (exec or plugin approvals)
    public const string OutputApprovalRequest = "output:approval:request";
    public const string OutputApprovalResolved = "output:approval:resolved";

    // Agent lifecycle / tool-call events surfaced from OpenClaw
    public const string OutputAgentEvent = "output:agent:event";

    // Errors
    public const string Error = "error";
}
