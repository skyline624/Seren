<script setup lang="ts">
import { computed } from 'vue'

/**
 * Dumb VU-meter for the Silero "is speech" probability. Renders a
 * horizontal SVG bar with two reference ticks (positive + negative
 * thresholds) so the user sees exactly where their voice sits relative
 * to the configured cutoffs. Intentionally stateless — the parent
 * passes `level` ticks; this component never touches the VAD or the
 * settings store (Single Responsibility).
 */
const props = withDefaults(defineProps<{
  /** Current Silero score, [0..1]. */
  level: number
  /** Speech-detected threshold, [0..1]. Drawn as a tick + accent fill above it. */
  positiveThreshold: number
  /** Back-to-silence threshold, [0..1]. Drawn as a second tick. */
  negativeThreshold: number
  /** Bar height in px. */
  height?: number
}>(), {
  height: 22,
})

const clampedLevel = computed(() => Math.max(0, Math.min(1, props.level)))
const positivePct = computed(() => Math.max(0, Math.min(1, props.positiveThreshold)) * 100)
const negativePct = computed(() => Math.max(0, Math.min(1, props.negativeThreshold)) * 100)
const fillPct = computed(() => clampedLevel.value * 100)

const isAboveSpeech = computed(() => clampedLevel.value >= props.positiveThreshold)
</script>

<template>
  <div class="vu-meter" :style="{ height: `${height}px` }" role="progressbar"
       :aria-valuenow="Math.round(clampedLevel * 100)" aria-valuemin="0" aria-valuemax="100">
    <div class="vu-meter__track">
      <div
        class="vu-meter__fill"
        :class="{ 'vu-meter__fill--active': isAboveSpeech }"
        :style="{ width: `${fillPct}%` }"
      />
      <div class="vu-meter__tick vu-meter__tick--negative" :style="{ left: `${negativePct}%` }" />
      <div class="vu-meter__tick vu-meter__tick--positive" :style="{ left: `${positivePct}%` }" />
    </div>
  </div>
</template>

<style scoped>
.vu-meter {
  width: 100%;
  display: flex;
  align-items: center;
}

.vu-meter__track {
  position: relative;
  width: 100%;
  height: 100%;
  border-radius: 6px;
  background: oklch(0.74 0.127 var(--seren-hue) / 0.08);
  border: 1px solid oklch(0.74 0.127 var(--seren-hue) / 0.18);
  overflow: hidden;
}

.vu-meter__fill {
  height: 100%;
  background: oklch(0.74 0.127 var(--seren-hue) / 0.45);
  transition: width 32ms linear, background 0.12s;
  border-radius: 6px 0 0 6px;
}

.vu-meter__fill--active {
  background: oklch(0.74 0.127 var(--seren-hue) / 0.85);
}

.vu-meter__tick {
  position: absolute;
  top: -1px;
  bottom: -1px;
  width: 2px;
  pointer-events: none;
}

.vu-meter__tick--negative {
  background: oklch(0.62 0.18 25 / 0.65);
}

.vu-meter__tick--positive {
  background: oklch(0.85 0.15 145 / 0.85);
}
</style>
