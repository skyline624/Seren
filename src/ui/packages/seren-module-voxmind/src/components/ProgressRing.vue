<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(defineProps<{
  /** Bytes already transferred. */
  done: number
  /** Total bytes expected. 0 = indeterminate, render a spinner. */
  total: number
  /** Stroke radius in pixels. Default 9 (matches a 24×24 row icon slot). */
  size?: number
}>(), {
  size: 9,
})

const stroke = 2
const dimension = computed(() => (props.size + stroke) * 2)
const circumference = computed(() => 2 * Math.PI * props.size)
const fraction = computed(() => {
  if (props.total <= 0) return 0
  return Math.min(1, Math.max(0, props.done / props.total))
})
const offset = computed(() => circumference.value * (1 - fraction.value))
const isIndeterminate = computed(() => props.total <= 0)
</script>

<template>
  <svg
    class="progress-ring"
    :class="{ 'progress-ring--spin': isIndeterminate }"
    :width="dimension"
    :height="dimension"
    :viewBox="`0 0 ${dimension} ${dimension}`"
    role="progressbar"
    :aria-valuenow="isIndeterminate ? undefined : Math.round(fraction * 100)"
    aria-valuemin="0"
    aria-valuemax="100"
  >
    <circle
      class="progress-ring__track"
      :cx="dimension / 2"
      :cy="dimension / 2"
      :r="size"
      fill="none"
      :stroke-width="stroke"
    />
    <circle
      class="progress-ring__bar"
      :cx="dimension / 2"
      :cy="dimension / 2"
      :r="size"
      fill="none"
      :stroke-width="stroke"
      :stroke-dasharray="circumference"
      :stroke-dashoffset="isIndeterminate ? circumference * 0.7 : offset"
      stroke-linecap="round"
    />
  </svg>
</template>

<style scoped>
.progress-ring {
  display: inline-block;
  transform: rotate(-90deg);
}

.progress-ring__track {
  stroke: oklch(0.74 0.127 var(--seren-hue) / 0.18);
}

.progress-ring__bar {
  stroke: oklch(0.74 0.127 var(--seren-hue) / 0.85);
  transition: stroke-dashoffset 0.2s linear;
}

.progress-ring--spin {
  animation: progress-ring-rotate 1.4s linear infinite;
}

@keyframes progress-ring-rotate {
  to {
    transform: rotate(270deg);
  }
}
</style>
