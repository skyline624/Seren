export { default as ChatPanel } from './components/ChatPanel.vue'
export { default as ChatErrorDialog } from './components/ChatErrorDialog.vue'
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
  useConnectionSettingsStore,
  useLlmSettingsStore,
  useVoiceSettingsStore,
} from './stores/settings'
export type { SerenSettings } from './stores/settings'
export type { ThemeMode, SupportedLocale } from './stores/settings/appearance'
export { DEFAULT_PRIMARY_HUE } from './stores/settings/appearance'
export type { ThinkingMode } from './stores/settings/llm'
export {
  POSITIVE_THRESHOLD_DEFAULT,
  NEGATIVE_THRESHOLD_DEFAULT,
  REDEMPTION_FRAMES_DEFAULT,
  SILERO_FRAME_MS,
  STT_LANGUAGE_DEFAULT,
} from './stores/settings/voice'
export type { SttLanguage } from './stores/settings/voice'
export { useAppearance } from './composables/useAppearance'
export { usePersistedRef } from './composables/usePersistedRef'
export {
  registerVoiceEnginePreference,
  getPreferredVoiceEngine,
} from './composables/voiceEnginePreference'
export {
  createChatHookRegistry,
  useSerenModulesRegistry,
  type SerenModuleSettingsTab,
} from './stores/modules'
export {
  serenChatHooksKey,
  serenModulesPlugin,
  type SerenModulesPluginOptions,
} from './plugins/serenModules'
export {
  PHASE_GAINS,
  useAvatarStateStore,
  type AvatarPhase,
  type LayerGains,
} from './stores/avatarState'
export { encodeWavBase64 } from './utils/wav-encoder'
