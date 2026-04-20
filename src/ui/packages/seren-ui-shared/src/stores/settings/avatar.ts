import { defineStore } from 'pinia'
import { usePersistedRef } from '../../composables/usePersistedRef'

export type AvatarMode = 'vrm' | 'live2d'
export type EyeTrackingMode = 'camera' | 'pointer' | 'off'

/**
 * Defaults picked to match the previous hardcoded values in VRMViewer.vue
 * + VRMOutlinePass.vue, so users who never touch the settings see exactly
 * the same scene as before.
 */
export const AVATAR_DEFAULTS = Object.freeze({
  mode: 'vrm' as AvatarMode,
  outlineEnabled: true,
  modelScale: 1,
  positionY: 0,
  rotationY: null as number | null,
  cameraDistance: 1.5,
  cameraHeight: 1.3,
  cameraFov: 50,
  ambientIntensity: 0.6,
  directionalIntensity: 0.8,
  eyeTrackingMode: 'camera' as EyeTrackingMode,
  outlineThickness: 0.003,
  outlineColor: '#000000',
  outlineAlpha: 0.8,
})

/**
 * Avatar renderer selection + per-renderer tuning knobs. Every field
 * persists to its own localStorage key through `usePersistedRef` so a
 * single setting change doesn't rewrite the whole bundle.
 *
 * `rotationY` is nullable so we can distinguish "auto-detect per VRM
 * version" (null → `useVRM` applies 180° for VRM 0.x, 0° for VRM 1.0)
 * from an explicit user override (number).
 */
export const useAvatarSettingsStore = defineStore('settings/avatar', () => {
  const mode = usePersistedRef<AvatarMode>('seren/avatar/mode', AVATAR_DEFAULTS.mode)
  const outlineEnabled = usePersistedRef<boolean>(
    'seren/avatar/outlineEnabled',
    AVATAR_DEFAULTS.outlineEnabled,
  )

  // Model transform
  const modelScale = usePersistedRef<number>('seren/avatar/modelScale', AVATAR_DEFAULTS.modelScale)
  const positionY = usePersistedRef<number>('seren/avatar/positionY', AVATAR_DEFAULTS.positionY)
  const rotationY = usePersistedRef<number | null>(
    'seren/avatar/rotationY',
    AVATAR_DEFAULTS.rotationY,
  )

  // Camera
  const cameraDistance = usePersistedRef<number>(
    'seren/avatar/cameraDistance',
    AVATAR_DEFAULTS.cameraDistance,
  )
  const cameraHeight = usePersistedRef<number>(
    'seren/avatar/cameraHeight',
    AVATAR_DEFAULTS.cameraHeight,
  )
  const cameraFov = usePersistedRef<number>('seren/avatar/cameraFov', AVATAR_DEFAULTS.cameraFov)

  // Lighting
  const ambientIntensity = usePersistedRef<number>(
    'seren/avatar/ambientIntensity',
    AVATAR_DEFAULTS.ambientIntensity,
  )
  const directionalIntensity = usePersistedRef<number>(
    'seren/avatar/directionalIntensity',
    AVATAR_DEFAULTS.directionalIntensity,
  )

  // Eye tracking
  const eyeTrackingMode = usePersistedRef<EyeTrackingMode>(
    'seren/avatar/eyeTrackingMode',
    AVATAR_DEFAULTS.eyeTrackingMode,
  )

  // Outline tuning
  const outlineThickness = usePersistedRef<number>(
    'seren/avatar/outlineThickness',
    AVATAR_DEFAULTS.outlineThickness,
  )
  const outlineColor = usePersistedRef<string>(
    'seren/avatar/outlineColor',
    AVATAR_DEFAULTS.outlineColor,
  )
  const outlineAlpha = usePersistedRef<number>(
    'seren/avatar/outlineAlpha',
    AVATAR_DEFAULTS.outlineAlpha,
  )

  function reset(): void {
    mode.value = AVATAR_DEFAULTS.mode
    outlineEnabled.value = AVATAR_DEFAULTS.outlineEnabled
    modelScale.value = AVATAR_DEFAULTS.modelScale
    positionY.value = AVATAR_DEFAULTS.positionY
    rotationY.value = AVATAR_DEFAULTS.rotationY
    cameraDistance.value = AVATAR_DEFAULTS.cameraDistance
    cameraHeight.value = AVATAR_DEFAULTS.cameraHeight
    cameraFov.value = AVATAR_DEFAULTS.cameraFov
    ambientIntensity.value = AVATAR_DEFAULTS.ambientIntensity
    directionalIntensity.value = AVATAR_DEFAULTS.directionalIntensity
    eyeTrackingMode.value = AVATAR_DEFAULTS.eyeTrackingMode
    outlineThickness.value = AVATAR_DEFAULTS.outlineThickness
    outlineColor.value = AVATAR_DEFAULTS.outlineColor
    outlineAlpha.value = AVATAR_DEFAULTS.outlineAlpha
  }

  return {
    mode,
    outlineEnabled,
    modelScale,
    positionY,
    rotationY,
    cameraDistance,
    cameraHeight,
    cameraFov,
    ambientIntensity,
    directionalIntensity,
    eyeTrackingMode,
    outlineThickness,
    outlineColor,
    outlineAlpha,
    reset,
  }
})
