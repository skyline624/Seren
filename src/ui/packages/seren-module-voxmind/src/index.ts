import { defineSerenModule, type SerenModuleDefinition } from '@seren/sdk'
import { registerVoiceEnginePreference } from '@seren/ui-shared'
import { useVoxMindSettingsStore } from './stores/voxmind'

export { useVoxMindSettingsStore, type VoxMindSttEngine } from './stores/voxmind'

const ICON_VOXMIND = `<svg viewBox="0 0 24 24"><path d="M12 14a3 3 0 0 0 3-3V5a3 3 0 1 0-6 0v6a3 3 0 0 0 3 3Zm5-3a5 5 0 0 1-10 0H5a7 7 0 0 0 6 6.92V21h2v-3.08A7 7 0 0 0 19 11Z"/><circle cx="18" cy="6" r="2.4" fill="currentColor"/></svg>`

/**
 * VoxMind module: ships the local STT engine selector. Pairs with the
 * server-side Seren.Modules.VoxMind multi-engine router. The user picks
 * Parakeet (fast, EN-biased) or Whisper (slower, FR-quality) from this
 * tab; the choice is persisted to localStorage and forwarded inline on
 * every voice request via the `sttEngine` payload field.
 */
const voxmindModule: SerenModuleDefinition = defineSerenModule({
  id: 'voxmind',
  version: '0.1.0',
  /**
   * Plug the user's preferred STT engine into seren-ui-shared's runtime
   * registry so ChatPanel's mic / dictate handlers can read it without
   * a static dep on this module. Pinia is guaranteed to be installed at
   * `install`-time because the SDK orders modules after the Pinia plugin.
   */
  install: () => {
    const teardown = registerVoiceEnginePreference(
      () => useVoxMindSettingsStore().sttEngine,
    )
    return teardown
  },
  settings: {
    labelKey: 'modules.voxmind.title',
    icon: ICON_VOXMIND,
    component: () => import('./SettingsTab.vue'),
    order: 35,
  },
  // i18n strings live in @seren/i18n under modules.voxmind.* — see
  // packages/seren-i18n/src/locales/{fr,en}.json. The SDK does not yet
  // merge `defineSerenModule.locales` into vue-i18n at install time, so
  // declaring them here would be dead code.
})

export default voxmindModule
