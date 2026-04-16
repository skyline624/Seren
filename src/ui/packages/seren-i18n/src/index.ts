import { createI18n as createVueI18n } from 'vue-i18n'
import en from './locales/en.json'
import fr from './locales/fr.json'

export type SupportedLocale = 'fr' | 'en'

export interface SerenI18nOptions {
  locale?: SupportedLocale
  fallbackLocale?: SupportedLocale
}

export type SerenMessageSchema = typeof fr

export const SUPPORTED_LOCALES: readonly SupportedLocale[] = ['fr', 'en'] as const

export function createSerenI18n(options: SerenI18nOptions = {}) {
  return createVueI18n<[SerenMessageSchema], SupportedLocale>({
    legacy: false,
    locale: options.locale ?? 'fr',
    fallbackLocale: options.fallbackLocale ?? 'en',
    messages: { fr, en },
  })
}

export { en, fr }
