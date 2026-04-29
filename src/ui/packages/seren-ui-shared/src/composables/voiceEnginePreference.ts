/**
 * Tiny runtime registry that lets the optional `@seren/module-voxmind`
 * package supply a per-user STT engine preference to ChatPanel without
 * creating a static cyclic dependency between `seren-ui-shared` (which
 * exports ChatPanel) and `seren-module-voxmind` (which depends on
 * seren-ui-shared for `usePersistedRef`).
 *
 * The module wires itself via its `install` hook (see
 * `SerenModuleDefinition.install`) and unregisters itself if the SDK
 * unmounts the module — keeping the contract honest both ways.
 */

type EnginePreferenceGetter = () => string | undefined

let currentGetter: EnginePreferenceGetter | null = null

/**
 * Called by an optional voice-settings module (e.g. `@seren/module-voxmind`)
 * during its `install` hook. Returns a teardown that the SDK calls when
 * the module is unmounted, keeping the registry symmetrical.
 */
export function registerVoiceEnginePreference(getter: EnginePreferenceGetter): () => void {
  currentGetter = getter
  return () => {
    if (currentGetter === getter) {
      currentGetter = null
    }
  }
}

/**
 * Reads the preferred STT engine if a module has registered one, else
 * <c>undefined</c> — letting the server fall back to its configured
 * default.
 */
export function getPreferredVoiceEngine(): string | undefined {
  return currentGetter?.()
}
