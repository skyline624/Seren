import type { VRM, VRMHumanBoneName } from '@pixiv/three-vrm'
import { MathUtils, Quaternion } from 'three'
import { onBeforeUnmount, ref } from 'vue'

/**
 * Procedural humanoid-bone animations used as a fallback for the
 * `<action:NAME>` markers emitted by the LLM (wave / nod / bow / shake).
 *
 * Kept deliberately lightweight: no external `.vrma` assets, no mixer, no
 * keyframe data — just a handful of bone rotations animated over a few
 * hundred milliseconds. The gesture is expressed as a per-frame override
 * function (see <c>applyOverride</c>) that the caller must invoke AFTER
 * the mixer advances (via <c>useVRM.onTickOverride</c>) so procedural
 * rotations win over the idle-animation clip.
 */
export type VRMGestureName = 'wave' | 'nod' | 'bow' | 'shake'

/**
 * Returns true when the action name is one of the procedural gestures
 * this composable knows how to render.
 */
export function isProceduralGesture(name: string | null | undefined): name is VRMGestureName {
  return name === 'wave' || name === 'nod' || name === 'bow' || name === 'shake'
}

interface GestureSpec {
  /** Humanoid bones the gesture mutates — snapshotted before + restored after. */
  bones: VRMHumanBoneName[]
  /** Wall-clock duration in milliseconds. */
  durationMs: number
  /**
   * Per-frame mutation. `t` is the normalized progress in `[0, 1]`.
   * Implementations write rotations relative to the base pose; the
   * composable restores the base automatically on completion.
   */
  apply: (t: number, vrm: VRM) => void
}

/**
 * Small procedural animation driver for VRM humanoid bones.
 *
 * Usage:
 * ```ts
 * const gestures = useVRMGestures(() => vrm.value)
 * onTickOverride(gestures.applyOverride) // registers the per-frame override
 * gestures.play('wave')
 * ```
 */
export function useVRMGestures(getVrm: () => VRM | null) {
  const isPlaying = ref(false)
  const currentGesture = ref<VRMGestureName | null>(null)
  let startedAt = 0
  let activeSpec: GestureSpec | null = null
  const baseRotations = new Map<VRMHumanBoneName, Quaternion>()

  const specs: Record<VRMGestureName, GestureSpec> = {
    wave: {
      bones: ['rightUpperArm', 'rightLowerArm', 'rightHand'],
      durationMs: 1800,
      apply: (t, vrm) => {
        const upperArm = vrm.humanoid?.getNormalizedBoneNode('rightUpperArm')
        const lowerArm = vrm.humanoid?.getNormalizedBoneNode('rightLowerArm')
        const hand = vrm.humanoid?.getNormalizedBoneNode('rightHand')
        if (!upperArm) return
        // Phase 1 (0 → 0.2): raise the arm to the side.
        // Phase 2 (0.2 → 0.8): wave at the wrist, arm held up.
        // Phase 3 (0.8 → 1): ease back down.
        const raiseT = t < 0.2 ? t / 0.2 : t > 0.8 ? 1 - (t - 0.8) / 0.2 : 1
        const raise = MathUtils.smoothstep(raiseT, 0, 1)
        upperArm.rotation.z = MathUtils.lerp(0, -1.9, raise)
        if (lowerArm) lowerArm.rotation.y = MathUtils.lerp(0, -0.5, raise)
        const waveActive = t > 0.2 && t < 0.8 ? 1 : 0
        if (hand) hand.rotation.z = Math.sin((t - 0.2) * Math.PI * 6) * 0.45 * waveActive
      },
    },
    nod: {
      bones: ['head', 'neck'],
      durationMs: 900,
      apply: (t, vrm) => {
        const head = vrm.humanoid?.getNormalizedBoneNode('head')
        if (!head) return
        // Two downward nods with decaying amplitude — ends back at 0.
        const amplitude = 0.35 * (1 - t)
        head.rotation.x = Math.sin(t * Math.PI * 4) * amplitude
      },
    },
    bow: {
      bones: ['spine', 'chest', 'head'],
      durationMs: 1600,
      apply: (t, vrm) => {
        const spine = vrm.humanoid?.getNormalizedBoneNode('spine')
        const head = vrm.humanoid?.getNormalizedBoneNode('head')
        if (!spine) return
        // Forward lean (sin over half-cycle): 0 → peak at 0.5 → 0.
        const bend = Math.sin(Math.min(t * Math.PI, Math.PI)) * 0.55
        spine.rotation.x = bend
        if (head) head.rotation.x = bend * 0.4 // head follows the torso
      },
    },
    shake: {
      bones: ['head', 'neck'],
      durationMs: 900,
      apply: (t, vrm) => {
        const head = vrm.humanoid?.getNormalizedBoneNode('head')
        if (!head) return
        const amplitude = 0.35 * (1 - t)
        head.rotation.y = Math.sin(t * Math.PI * 6) * amplitude
      },
    },
  }

  function snapshot(vrm: VRM, bones: readonly VRMHumanBoneName[]): void {
    baseRotations.clear()
    for (const name of bones) {
      const node = vrm.humanoid?.getNormalizedBoneNode(name)
      if (node) baseRotations.set(name, node.quaternion.clone())
    }
  }

  function restore(): void {
    const vrm = getVrm()
    if (!vrm) {
      baseRotations.clear()
      return
    }
    for (const [name, quaternion] of baseRotations) {
      const node = vrm.humanoid?.getNormalizedBoneNode(name)
      if (node) node.quaternion.copy(quaternion)
    }
    baseRotations.clear()
  }

  function cancel(): void {
    if (activeSpec) {
      restore()
    }
    activeSpec = null
    currentGesture.value = null
    isPlaying.value = false
  }

  function play(name: VRMGestureName): void {
    const vrm = getVrm()
    const spec = specs[name]
    if (!vrm || !spec) return

    cancel()
    snapshot(vrm, spec.bones)
    activeSpec = spec
    currentGesture.value = name
    startedAt = performance.now()
    isPlaying.value = true
  }

  /**
   * Per-frame override to register with `useVRM.onTickOverride`. Applies
   * the active gesture over any rotations the mixer just wrote for the
   * affected bones.
   */
  function applyOverride(_delta: number): void {
    if (!activeSpec) return
    const vrm = getVrm()
    if (!vrm) {
      cancel()
      return
    }
    const elapsed = performance.now() - startedAt
    const t = Math.min(1, elapsed / activeSpec.durationMs)
    activeSpec.apply(t, vrm)
    if (t >= 1) {
      cancel()
    }
  }

  onBeforeUnmount(cancel)

  return {
    play,
    cancel,
    /** Per-frame override — wire via `useVRM.onTickOverride(gestures.applyOverride)`. */
    applyOverride,
    /** Read-only: name of the gesture currently playing, or `null`. */
    currentGesture,
    /** Read-only: `true` between `play()` and the end of the clip. */
    isPlaying,
  }
}
