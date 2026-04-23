/**
 * Gated structured logger for the avatar AI (idle scheduler + emotion
 * classifier). Enabled at runtime with `window.__SEREN_DEBUG_AVATAR__
 * = true` in the browser console — noisy otherwise.
 *
 * Kept deliberately small: no level enum, no remote sink, no buffers.
 * When a real observability need emerges (metrics over time, export
 * to OTel JS), this file is the one place to evolve.
 */

interface SerenDebugWindow extends Window {
  __SEREN_DEBUG_AVATAR__?: boolean
}

/**
 * Log a structured debug entry when the global flag is truthy.
 * Otherwise no-op (zero cost, no stringification).
 *
 * @param scope - short tag identifying the producer (`'idle'` /
 *   `'classifier'`). Kept free-form to avoid a premature enum.
 * @param event - short label for the event (`'trigger'`, `'skip_classify'`, …).
 * @param details - optional structured payload.
 */
export function avatarDebugLog(
  scope: string,
  event: string,
  details?: Record<string, unknown>,
): void {
  if (typeof window === 'undefined') return
  const flag = (window as SerenDebugWindow).__SEREN_DEBUG_AVATAR__
  if (!flag) return

  // Prefix keeps lines searchable in the browser console with a single
  // filter (`[avatar-ai]`). `info` rather than `debug` so Chrome shows
  // the line at the default log level — users don't need to toggle
  // Verbose just to see AI-driven avatar events.
  // eslint-disable-next-line no-console
  console.info(`[avatar-ai] ${scope} ${event}`, details ?? {})
}
