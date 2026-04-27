import { defineSerenModule, type SerenModuleDefinition } from '@seren/sdk'

const ICON_VOICE = `<svg viewBox="0 0 24 24"><path d="M12 14a3 3 0 0 0 3-3V5a3 3 0 1 0-6 0v6a3 3 0 0 0 3 3Zm5-3a5 5 0 0 1-10 0H5a7 7 0 0 0 6 6.92V21h2v-3.08A7 7 0 0 0 19 11Z"/></svg>`

/**
 * Audio module: ships the voice-input settings tab (VAD threshold). Pilot
 * implementation of the SerenModuleDefinition contract — pairs with the
 * server-side Seren.Modules.Audio. The settings component re-uses the
 * `useVoiceSettingsStore` from @seren/ui-shared to keep the persistence
 * key stable for end users (no migration needed).
 */
const audioModule: SerenModuleDefinition = defineSerenModule({
  id: 'audio',
  version: '0.1.0',
  settings: {
    labelKey: 'settings.nav.voice',
    icon: ICON_VOICE,
    component: () => import('./SettingsTab.vue'),
    order: 30,
  },
  locales: {
    en: {
      audio: {
        title: 'Voice',
        vadThreshold: 'Voice detection threshold',
        vadThresholdHint: 'Higher = less sensitive. 0.5 is a reasonable starting point.',
      },
    },
    fr: {
      audio: {
        title: 'Voix',
        vadThreshold: 'Seuil de détection vocale',
        vadThresholdHint: 'Plus élevé = moins sensible. 0.5 est un bon point de départ.',
      },
    },
  },
})

export default audioModule
