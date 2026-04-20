export { default as ChatPanel } from './components/ChatPanel.vue'
export { default as AvatarStage } from './components/AvatarStage.vue'
export { default as CharacterSelector } from './components/CharacterSelector.vue'
export { default as SettingsPanel } from './components/SettingsPanel.vue'
export { useChatStore } from './stores/chat'
export type { ChatMessage, InitClientOptions } from './stores/chat'
export { useCharacterStore } from './stores/character'
export type { CharacterDto, CreateCharacterInput } from './stores/character'
export {
  useSettingsStore,
  useAppearanceSettingsStore,
  useAvatarSettingsStore,
  useConnectionSettingsStore,
  useLlmSettingsStore,
  useVoiceSettingsStore,
} from './stores/settings'
export type { SerenSettings } from './stores/settings'
export type { ThemeMode, SupportedLocale } from './stores/settings/appearance'
export { DEFAULT_PRIMARY_HUE } from './stores/settings/appearance'
export type { AvatarMode } from './stores/settings/avatar'
export type { ThinkingMode } from './stores/settings/llm'
export { useAppearance } from './composables/useAppearance'
export { usePersistedRef } from './composables/usePersistedRef'
export { encodeWavBase64 } from './utils/wav-encoder'
