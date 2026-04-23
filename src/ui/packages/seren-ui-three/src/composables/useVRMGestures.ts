import type { VRM, VRMHumanBoneName } from '@pixiv/three-vrm'
import { Euler, MathUtils, Quaternion } from 'three'
import { onBeforeUnmount, ref } from 'vue'

/**
 * Baseline humanoid pose applied EVERY tick to bones the mixer
 * doesn't animate (typically the shoulders — <c>idle_loop.vrma</c>
 * has no <c>UpperArm</c> tracks). Without this baseline, these bones
 * would stay at the model's bind pose — identity quaternion on most
 * VRMs, which reads as a literal T-pose.
 * <br/>
 * Values are Euler triplets in the order the VRM humanoid normalizer
 * produces; +Z on <c>leftUpperArm</c> rotates the arm downward along
 * the body's left, −Z mirrors it on the right. Tuned for the default
 * VRM rig; model-specific tweaks belong in a future settings store,
 * not inline overrides.
 */
const REST_POSE: Readonly<Partial<Record<VRMHumanBoneName, readonly [number, number, number]>>>
  = Object.freeze({
    // Empirical on the default Seren VRM: +Z on leftUpperArm raises
    // the arm upward (V-pose), so the "arms-hanging-down" rest needs
    // a NEGATIVE Z on the left. Right shoulder's local Z is mirrored
    // by the humanoid normalizer, so the same visual motion is +Z.
    //
    // Tuning notes :
    //  - Z magnitude 0.95 (≈ 54°) leaves a small gap between the arm
    //    and the torso — 1.1 was too aggressive, arms stuck to body.
    //  - Tiny forward X (0.15) pulls the arms slightly in front of
    //    the hip plane — natural standing resting posture instead of
    //    the "garde-à-vous" stiff vertical look.
    //  - Slight forearm bend + palm-inward rotation on the hands
    //    breaks the "straight stick" silhouette.
    leftUpperArm: [0.15, 0, -0.95] as const,
    rightUpperArm: [0.15, 0, 0.95] as const,
    leftLowerArm: [0, 0.25, 0] as const,
    rightLowerArm: [0, -0.25, 0] as const,
    leftHand: [0, 0, -0.1] as const,
    rightHand: [0, 0, 0.1] as const,
  })

/** Z-rotation component of the rest pose for a given bone, or 0 when
 *  the bone has no rest override. Used by gesture specs (wave,
 *  stretch_small) to compose their motion ON TOP of the rest pose
 *  rather than snapping back to identity at t=1. */
function restZ(bone: VRMHumanBoneName): number {
  return REST_POSE[bone]?.[2] ?? 0
}

/** Bones in {@link REST_POSE}, cached as an array so the tick loop
 *  doesn't allocate on every frame. */
const REST_POSE_BONES = Object.freeze(Object.keys(REST_POSE) as VRMHumanBoneName[])

/**
 * Gated debug logger mirroring `@seren/ui-shared/avatarDebugLog`.
 * Inlined (not imported) to keep this package free of a circular
 * dependency on seren-ui-shared. Flip
 * `window.__SEREN_DEBUG_AVATAR__ = true` in devtools to see every
 * gesture phase; no-op otherwise, zero cost in production.
 */
interface SerenDebugWindow extends Window {
  __SEREN_DEBUG_AVATAR__?: boolean
}
function gestureDebugLog(event: string, details?: Record<string, unknown>): void {
  if (typeof window === 'undefined') return
  const flag = (window as SerenDebugWindow).__SEREN_DEBUG_AVATAR__
  if (!flag) return
  // `info` instead of `debug` so Chrome's default console level
  // surfaces the message without forcing the user to switch to
  // Verbose. Still filtered out unless the debug flag is flipped.
  // eslint-disable-next-line no-console
  console.info(`[avatar-ai] gesture ${event}`, details ?? {})
}

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
export type VRMGestureName =
  | 'wave' | 'nod' | 'bow' | 'shake'
  // Idle-variant gestures fired by `useIdleAnimationScheduler` during
  // conversation pauses. Kept in the same catalog as the LLM-driven
  // gestures so the renderer has exactly one dispatch path (DRY).
  | 'look_left' | 'look_right' | 'look_up' | 'look_down'
  | 'blink_double' | 'breath_deep' | 'stretch_small'

const PROCEDURAL_GESTURES: ReadonlySet<string> = new Set<VRMGestureName>([
  'wave', 'nod', 'bow', 'shake',
  'look_left', 'look_right', 'look_up', 'look_down',
  'blink_double', 'breath_deep', 'stretch_small',
])

/**
 * Returns true when the action name is one of the procedural gestures
 * this composable knows how to render.
 */
export function isProceduralGesture(name: string | null | undefined): name is VRMGestureName {
  return typeof name === 'string' && PROCEDURAL_GESTURES.has(name)
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
        // Start + end at the shoulder's rest rotation (arm down),
        // peak at the canonical "waving" angle. Prevents the T-pose
        // that identity-at-t=1 would cause on models whose idle
        // animation doesn't touch the shoulder.
        const raiseT = t < 0.2 ? t / 0.2 : t > 0.8 ? 1 - (t - 0.8) / 0.2 : 1
        const raise = MathUtils.smoothstep(raiseT, 0, 1)
        // Peak sign mirrors the rest-pose sign convention: the
        // humanoid normalizer's mirrored Z axis on the right arm
        // means +1.9 raises the arm up to waving height, while -1.9
        // would push it down behind the back.
        upperArm.rotation.z = MathUtils.lerp(restZ('rightUpperArm'), 1.9, raise)
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
    // ── Idle variants (Tier 1) ────────────────────────────────────
    // Each is a short, low-amplitude bone rotation with a smooth
    // in/out curve (sin over half period) so the avatar returns to
    // the base pose at t=1 without snapping.
    look_left: {
      bones: ['head', 'neck'],
      durationMs: 1500,
      apply: (t, vrm) => {
        const head = vrm.humanoid?.getNormalizedBoneNode('head')
        if (!head) return
        head.rotation.y = Math.sin(t * Math.PI) * 0.35
      },
    },
    look_right: {
      bones: ['head', 'neck'],
      durationMs: 1500,
      apply: (t, vrm) => {
        const head = vrm.humanoid?.getNormalizedBoneNode('head')
        if (!head) return
        head.rotation.y = -Math.sin(t * Math.PI) * 0.35
      },
    },
    look_up: {
      bones: ['head', 'neck'],
      durationMs: 1200,
      apply: (t, vrm) => {
        const head = vrm.humanoid?.getNormalizedBoneNode('head')
        if (!head) return
        head.rotation.x = -Math.sin(t * Math.PI) * 0.25
      },
    },
    look_down: {
      bones: ['head', 'neck'],
      durationMs: 1200,
      apply: (t, vrm) => {
        const head = vrm.humanoid?.getNormalizedBoneNode('head')
        if (!head) return
        head.rotation.x = Math.sin(t * Math.PI) * 0.3
      },
    },
    blink_double: {
      // Expressions handled separately in useVRM — here we only nudge
      // the head ever so slightly so the motion reads as "alive".
      bones: ['head'],
      durationMs: 600,
      apply: (t, vrm) => {
        const head = vrm.humanoid?.getNormalizedBoneNode('head')
        if (!head) return
        head.rotation.x = Math.sin(t * Math.PI * 2) * 0.05
      },
    },
    breath_deep: {
      bones: ['spine', 'chest'],
      durationMs: 2000,
      apply: (t, vrm) => {
        const spine = vrm.humanoid?.getNormalizedBoneNode('spine')
        const chest = vrm.humanoid?.getNormalizedBoneNode('chest')
        if (!spine) return
        // Single deep inhale/exhale cycle. Spine tips back very slightly,
        // chest expands via a faint rotation.
        const phase = Math.sin(t * Math.PI) * 0.08
        spine.rotation.x = -phase
        if (chest) chest.rotation.x = -phase * 0.4
      },
    },
    stretch_small: {
      bones: ['leftUpperArm', 'rightUpperArm', 'spine'],
      durationMs: 1800,
      apply: (t, vrm) => {
        const left = vrm.humanoid?.getNormalizedBoneNode('leftUpperArm')
        const right = vrm.humanoid?.getNormalizedBoneNode('rightUpperArm')
        const spine = vrm.humanoid?.getNormalizedBoneNode('spine')
        // Shoulder roll + slight torso lift. Rest-pose-offset so the
        // arms start + end at the relaxed shoulder rotation instead
        // of snapping to the model's bind (T-)pose at t=0/t=1.
        const lift = Math.sin(t * Math.PI) * 0.25
        if (left) left.rotation.z = restZ('leftUpperArm') + lift * 0.6
        if (right) right.rotation.z = restZ('rightUpperArm') - lift * 0.6
        if (spine) spine.rotation.x = -lift * 0.15
      },
    },
  }

  // Reusable scratch objects to avoid per-frame allocations inside
  // the tick override.
  const restEuler = new Euler()
  const restQuaternion = new Quaternion()

  /**
   * Writes the baseline posture for bones listed in
   * <see cref="REST_POSE"/>. Called on every tick BEFORE the active
   * gesture (if any) so the gesture can read/modify an already-posed
   * bone instead of identity. Silent no-op for bones not in the
   * override list — the mixer's animation is preserved intact.
   */
  function applyRestPose(vrm: VRM): void {
    for (const name of REST_POSE_BONES) {
      const triple = REST_POSE[name]
      if (!triple) continue
      const node = vrm.humanoid?.getNormalizedBoneNode(name)
      if (!node) continue
      restEuler.set(triple[0], triple[1], triple[2])
      restQuaternion.setFromEuler(restEuler)
      node.quaternion.copy(restQuaternion)
    }
  }

  function snapshot(vrm: VRM, bones: readonly VRMHumanBoneName[]): void {
    baseRotations.clear()
    for (const name of bones) {
      const node = vrm.humanoid?.getNormalizedBoneNode(name)
      if (node) baseRotations.set(name, node.quaternion.clone())
    }
    gestureDebugLog('snapshot', { bones: Array.from(baseRotations.keys()) })
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
    gestureDebugLog('restore', { bones: Array.from(baseRotations.keys()) })
    baseRotations.clear()
  }

  // Natural completion — the gesture's `apply(1, vrm)` already returned
  // every targeted bone rotation to 0 (Euler), so we just clear state
  // and let the mixer (and any subsequent gesture) take over. We
  // DON'T call restore() here : that would re-apply the snapshotted
  // quaternions captured 1-2s ago, causing a visible pop on idle-
  // sparse models (e.g. idle_loop.vrma that doesn't animate the arms).
  function finishNaturally(): void {
    baseRotations.clear()
    activeSpec = null
    currentGesture.value = null
    isPlaying.value = false
    gestureDebugLog('finish', { reason: 'natural' })
  }

  // Preemptive abort — called when a new gesture preempts the current
  // one, when the VRM unmounts, or when `getVrm()` returns null
  // mid-tick. Restore is required here so the preempting gesture
  // starts from a clean base rather than accumulating over a partial
  // override.
  function cancel(): void {
    if (activeSpec) {
      restore()
    }
    activeSpec = null
    currentGesture.value = null
    isPlaying.value = false
    gestureDebugLog('cancel', { reason: 'preemption' })
  }

  function play(name: VRMGestureName): void {
    const vrm = getVrm()
    const spec = specs[name]
    if (!vrm || !spec) {
      gestureDebugLog('play_skipped', { name, vrmReady: !!vrm, specExists: !!spec })
      return
    }

    cancel()
    snapshot(vrm, spec.bones)
    activeSpec = spec
    currentGesture.value = name
    startedAt = performance.now()
    isPlaying.value = true
    gestureDebugLog('play', { name, durationMs: spec.durationMs })
  }

  /**
   * Per-frame override to register with `useVRM.onTickOverride`.
   * Runs every tick — not only when a gesture is playing — so the
   * baseline rest pose always wins on bones the mixer doesn't
   * animate. Active gesture layers on top of the baseline.
   */
  function applyOverride(_delta: number): void {
    const vrm = getVrm()
    if (!vrm) {
      if (activeSpec) cancel()
      return
    }

    // Baseline: un-T-pose the shoulders every frame.
    applyRestPose(vrm)

    if (!activeSpec) return
    const elapsed = performance.now() - startedAt
    const t = Math.min(1, elapsed / activeSpec.durationMs)
    activeSpec.apply(t, vrm)
    if (t >= 1) {
      finishNaturally()
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
