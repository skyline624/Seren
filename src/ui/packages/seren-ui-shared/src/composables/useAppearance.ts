import { storeToRefs } from 'pinia'
import { onBeforeUnmount, watchEffect } from 'vue'
import { useAppearanceSettingsStore, type ThemeMode } from '../stores/settings/appearance'

/**
 * Applies the theme + primary-hue settings to `document.documentElement`
 * and keeps them in sync with user changes. Should be called once from
 * the root app component (`App.vue`) during setup so the first paint
 * already reflects the stored preferences.
 *
 * Locale propagation to vue-i18n is deliberately **not** handled here
 * to keep this package free of a hard dependency on vue-i18n; the
 * hosting app is expected to watch `useAppearanceSettingsStore().locale`
 * and forward it into its own i18n instance.
 *
 * Cleans up the `prefers-color-scheme` media-query listener on unmount.
 */
export function useAppearance(): void {
  const { themeMode, primaryHue } = storeToRefs(useAppearanceSettingsStore())

  const mq = typeof window !== 'undefined'
    ? window.matchMedia('(prefers-color-scheme: dark)')
    : null

  function applyTheme(): void {
    if (typeof document === 'undefined') return
    document.documentElement.dataset.theme = resolveTheme(themeMode.value, mq)
  }

  function applyHue(): void {
    if (typeof document === 'undefined') return
    document.documentElement.style.setProperty('--seren-hue', String(primaryHue.value))
  }

  // Initial apply — synchronous so the first paint is correct.
  applyTheme()
  applyHue()

  watchEffect(applyTheme)
  watchEffect(applyHue)

  // `auto` mode must follow the OS: a theme change there re-applies the
  // data-theme attribute. Explicit light/dark simply ignores the event.
  const osListener = (): void => {
    if (themeMode.value === 'auto') applyTheme()
  }
  mq?.addEventListener('change', osListener)

  onBeforeUnmount(() => {
    mq?.removeEventListener('change', osListener)
  })
}

function resolveTheme(mode: ThemeMode, mq: MediaQueryList | null): 'light' | 'dark' {
  if (mode === 'light') return 'light'
  if (mode === 'dark') return 'dark'
  return mq?.matches ? 'dark' : 'light'
}
