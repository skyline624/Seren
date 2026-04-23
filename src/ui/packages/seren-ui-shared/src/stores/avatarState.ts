import { defineStore } from 'pinia'
import { computed } from 'vue'
import { useChatStore } from './chat'

/**
 * Finite set of "phases" the avatar can be in. Fully derived from
 * chat-store flags — there is no mutable source of truth here, which
 * makes it impossible for the UI state to drift out of sync with the
 * actual conversation pipeline.
 */
export type AvatarPhase = 'idle' | 'listening' | 'thinking' | 'talking' | 'reactive'

/**
 * Amplitude / frequency modulators applied to every animation layer
 * per avatar phase. Anchored to `1.0` in `idle`, so a gain > 1 means
 * "more of this behavior" (more sway, more blink, etc.) and < 1
 * means "less". `headTilt` is in radians — added directly, not
 * multiplied.
 */
export interface LayerGains {
  /** Multiplier on body-sway amplitude. 1 = baseline Animaze numbers. */
  bodySway: number
  /** Multiplier on blink *frequency*. 1 = 1-6s cadence ; 1.2 = ~17% more
   *  blinks per minute. Internally the composable divides its roll by
   *  this value. */
  blink: number
  /** Multiplier on saccade *frequency*. 1 = 0.4-2.5s cadence ;
   *  1.4 = ~40% more saccades per minute (nervous-thinking fidgets). */
  saccade: number
  /** Constant X-rotation applied to `neck` (radians). Negative = head
   *  forward, positive = head back. 0 in every phase except `thinking`. */
  headTilt: number
}

/**
 * Phase → gains table. Values tuned empirically against a typical
 * VRM 1.0 rig ; callers can override via settings later. Designed so
 * `idle` is the identity (all 1, tilt 0) and other phases lean
 * gently in one direction or the other.
 */
export const PHASE_GAINS: Readonly<Record<AvatarPhase, LayerGains>> = Object.freeze({
  idle: { bodySway: 1.0, saccade: 1.0, blink: 1.0, headTilt: 0 },
  listening: { bodySway: 0.9, saccade: 0.8, blink: 1.0, headTilt: 0 },
  thinking: { bodySway: 0.6, saccade: 1.4, blink: 0.7, headTilt: -0.12 },
  talking: { bodySway: 1.2, saccade: 0.9, blink: 1.1, headTilt: 0 },
  reactive: { bodySway: 1.0, saccade: 1.0, blink: 1.0, headTilt: 0 },
})

/**
 * Compute the avatar's phase from the chat-store flags. Pure
 * derivation — priority order matches user-perceived dominance :
 *   talking > thinking > reactive > listening > idle
 * so that a mid-stream action marker doesn't steal the spotlight
 * from the assistant's talking animation.
 */
export const useAvatarStateStore = defineStore('avatarState', () => {
  const chat = useChatStore()

  const phase = computed<AvatarPhase>(() => {
    if (chat.isStreaming || chat.isSpeaking) return 'talking'
    if (chat.isThinking) return 'thinking'
    if (chat.currentAction) return 'reactive'
    if (chat.connectionStatus === 'ready') return 'listening'
    return 'idle'
  })

  const gains = computed<LayerGains>(() => PHASE_GAINS[phase.value])

  return { phase, gains }
})
