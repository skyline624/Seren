import { defineStore } from 'pinia'
import { usePersistedRef } from '../../composables/usePersistedRef'

export type ThemeMode = 'auto' | 'light' | 'dark'
export type SupportedLocale = 'fr' | 'en'

/** Default hue (220°) lines up with the current `--airi-teal` palette. */
export const DEFAULT_PRIMARY_HUE = 220

/**
 * Visual appearance: theme mode (auto follows OS), primary accent hue
 * 0-360° which drives the `--seren-hue` CSS variable, and locale used
 * by the i18n layer. Consumed by `useAppearance()` which writes the
 * derived CSS custom properties onto `:root`.
 */
export const useAppearanceSettingsStore = defineStore('settings/appearance', () => {
  const themeMode = usePersistedRef<ThemeMode>('seren/appearance/themeMode', 'auto')
  const primaryHue = usePersistedRef<number>('seren/appearance/primaryHue', DEFAULT_PRIMARY_HUE)
  const locale = usePersistedRef<SupportedLocale>('seren/appearance/locale', 'fr')

  function reset(): void {
    themeMode.value = 'auto'
    primaryHue.value = DEFAULT_PRIMARY_HUE
    locale.value = 'fr'
  }

  return { themeMode, primaryHue, locale, reset }
})
