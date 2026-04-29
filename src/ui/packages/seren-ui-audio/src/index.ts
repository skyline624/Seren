export { useVoiceInput, VAD_SAMPLE_RATE } from './composables/useVoiceInput'
export type {
  UseVoiceInputOptions,
  VoiceInputApi,
  VoiceInputMode,
} from './composables/useVoiceInput'

export { useAudioDevices } from './composables/useAudioDevices'
export type { UseAudioDevicesApi } from './composables/useAudioDevices'

export type { AudioConstraints } from './strategies/types'

export { PlaybackManager } from './playback/PlaybackManager'

export type {
  AudioChunk,
  PlaybackOptions,
  VisemeFrame,
  VadWorkerMessage,
  VadWorkerResponse,
} from './types/audio'