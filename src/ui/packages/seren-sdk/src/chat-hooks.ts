/**
 * Hooks fired by the chat pipeline at well-defined points. Modules can
 * subscribe to instrument the conversation (e.g. inject context before
 * send, mirror tokens to a sidecar UI, capture final assistant text for
 * analytics).
 *
 * The contract starts intentionally narrow — four hooks, all optional.
 * Adding more later is non-breaking; removing one would be, so we hold
 * the line until concrete use cases justify it.
 */
export interface ChatHooks {
  /**
   * Fired before the SDK serialises and sends a user text message.
   * Async hooks block the send, so keep them fast (sub-100ms typically).
   */
  onBeforeSend?: (message: { text: string, sessionId: string }) => Promise<void> | void

  /**
   * Fired right after the SDK has flushed the user message onto the
   * WebSocket. Synchronous, fire-and-forget.
   */
  onAfterSend?: (message: { text: string, sessionId: string }) => void

  /**
   * Fired for each output token chunk arriving from the assistant. Called
   * many times per turn; do as little work as possible per call. Tokens
   * arrive in stream order; markup tags (<think>, <emotion:>, …) have
   * already been stripped upstream — these are user-visible characters.
   */
  onTokenLiteral?: (token: string) => void

  /**
   * Fired once per turn after the final token has been rendered. The
   * `text` field carries the full reassembled assistant message.
   */
  onAssistantResponseEnd?: (final: { text: string }) => void
}

/**
 * Registry that collects per-module hook bundles. The chat pipeline
 * iterates registered hooks in registration order at each emission.
 */
export interface ChatHookRegistry {
  /**
   * Registers a hook bundle for the given module id. Returns a cleanup
   * function that unregisters every hook in the bundle — primarily
   * useful for tests; production modules typically register once and
   * never tear down.
   */
  register: (moduleId: string, hooks: ChatHooks) => () => void

  /** Snapshot of the currently-registered hook bundles, keyed by module id. */
  readonly entries: ReadonlyArray<readonly [string, ChatHooks]>
}
