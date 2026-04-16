export { useVoiceInput, VAD_SAMPLE_RATE } from './composables/useVoiceInput'
export type { UseVoiceInputOptions } from './composables/useVoiceInput'

export { PlaybackManager } from './playback/PlaybackManager'

export type {
  AudioChunk,
  PlaybackOptions,
  VisemeFrame,
  VadWorkerMessage,
  VadWorkerResponse,
} from './types/audio'