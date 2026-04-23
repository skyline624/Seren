export { default as VRMLookAtController } from './components/VRMLookAtController.vue'
export { default as VRMOutlinePass } from './components/VRMOutlinePass.vue'
export { default as VRMViewer } from './components/VRMViewer.vue'
export { applyViseme, useLipsync, type VisemeTrackFrame } from './composables/useLipsync'
export { useVRM } from './composables/useVRM'
export { useVRMAnimation } from './composables/useVRMAnimation'
export { useVRMLookAt, type EyeTrackingMode } from './composables/useVRMLookAt'
export {
  BLINK_DURATION,
  BLINK_EXPRESSION_NAME,
  BLINK_MAX_INTERVAL,
  BLINK_MIN_INTERVAL,
  useBlink,
  type BlinkController,
  type BlinkOptions,
} from './composables/useBlink'
export {
  SACCADE_JITTER,
  SACCADE_MAX_INTERVAL,
  SACCADE_MIN_INTERVAL,
  useIdleEyeSaccades,
  type SaccadeController,
  type SaccadeOptions,
} from './composables/useIdleEyeSaccades'
export {
  BREATH_AMPLITUDE_DEFAULT,
  BREATH_PERIOD_DEFAULT,
  HIP_AMPLITUDE_DEFAULT,
  HIP_PERIOD_DEFAULT,
  useIdleBodySway,
  WEIGHT_AMPLITUDE_DEFAULT,
  WEIGHT_PERIOD_DEFAULT,
  type BodySwayController,
  type BodySwayOptions,
} from './composables/useIdleBodySway'
export {
  DEFAULT_EMOTIONS,
  EMOTION_ALIASES,
  resolveEmotionKey,
  useVRMEmote,
  type EmotionPreset,
  type VRMEmoteController,
  type VRMEmoteOptions,
} from './composables/useVRMEmote'
