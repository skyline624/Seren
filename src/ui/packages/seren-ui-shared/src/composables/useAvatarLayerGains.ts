import { computed, type ComputedRef } from 'vue'
import { storeToRefs } from 'pinia'
import { useAvatarStateStore, type LayerGains } from '../stores/avatarState'

/**
 * Convenience wrapper : returns a `ComputedRef<LayerGains>` derived
 * from the avatar-state phase. Equivalent to reading
 * `useAvatarStateStore().gains` directly — exists as a composable so
 * renderers don't need to import the store surface just to get this
 * one thing.
 *
 * Usage :
 * ```ts
 * const gains = useAvatarLayerGains()
 * // gains.value → { bodySway, blink, saccade, headTilt }
 * ```
 */
export function useAvatarLayerGains(): ComputedRef<LayerGains> {
  const store = useAvatarStateStore()
  const { phase, gains } = storeToRefs(store)
  // Returning a fresh computed so the consumer can plug it straight
  // into a component prop binding without worrying about the store
  // ref's mutability.
  return computed<LayerGains>(() => {
    void phase.value  // force-read for reactive tracking
    return gains.value
  })
}
