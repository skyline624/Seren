<script setup lang="ts">
import { computed } from 'vue'
import { storeToRefs } from 'pinia'
import { useAvatarSettingsStore, type EyeTrackingMode } from '../../stores/settings/avatar'

const store = useAvatarSettingsStore()
const {
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
} = storeToRefs(store)

// `rotationY` is nullable (null = auto-detect per VRM version).
// The range input can't bind to null directly, so we go through a
// writable computed that treats `null` as the mid-point 0° and flips
// back to `null` when the user presses the "auto" button.
const rotationYSlider = computed<number>({
  get: () => rotationY.value ?? 0,
  set: (v) => { rotationY.value = v },
})

function resetRotationAuto(): void {
  rotationY.value = null
}

const eyeModes: Array<{ value: EyeTrackingMode, labelKey: string }> = [
  { value: 'camera', labelKey: 'settings.avatar.eye.camera' },
  { value: 'pointer', labelKey: 'settings.avatar.eye.pointer' },
  { value: 'off', labelKey: 'settings.avatar.eye.off' },
]
</script>

<template>
  <section class="settings-section">
    <h3 class="settings-section__title">{{ $t('settings.avatar.title') }}</h3>

    <!-- Mode -->
    <div class="settings-field">
      <label class="settings-field__label" for="avatar-mode">
        {{ $t('settings.avatar.mode') }}
      </label>
      <select id="avatar-mode" v-model="mode" class="settings-field__select">
        <option value="vrm">{{ $t('settings.avatar.modeVrm') }}</option>
        <option value="live2d">{{ $t('settings.avatar.modeLive2d') }}</option>
      </select>
    </div>

    <template v-if="mode === 'vrm'">
      <!-- Model transform -->
      <h4 class="settings-field__subtitle">{{ $t('settings.avatar.model') }}</h4>
      <div class="settings-field">
        <label class="settings-field__label" for="avatar-scale">
          {{ $t('settings.avatar.modelScale') }}: {{ modelScale.toFixed(2) }}
        </label>
        <input
          id="avatar-scale"
          v-model.number="modelScale"
          type="range"
          min="0.5"
          max="2"
          step="0.05"
          class="settings-field__range"
        >
      </div>
      <div class="settings-field">
        <label class="settings-field__label" for="avatar-posY">
          {{ $t('settings.avatar.positionY') }}: {{ positionY.toFixed(2) }}
        </label>
        <input
          id="avatar-posY"
          v-model.number="positionY"
          type="range"
          min="-1"
          max="1"
          step="0.05"
          class="settings-field__range"
        >
      </div>
      <div class="settings-field">
        <label class="settings-field__label" for="avatar-rotY">
          {{ $t('settings.avatar.rotationY') }}:
          {{ rotationY === null ? $t('settings.avatar.rotationAuto') : `${((rotationY * 180) / Math.PI).toFixed(0)}°` }}
        </label>
        <div class="settings-field__row">
          <input
            id="avatar-rotY"
            v-model.number="rotationYSlider"
            type="range"
            :min="-Math.PI"
            :max="Math.PI"
            step="0.05"
            class="settings-field__range"
          >
          <button type="button" class="settings-section__btn" @click="resetRotationAuto">
            {{ $t('settings.avatar.rotationAutoBtn') }}
          </button>
        </div>
      </div>

      <!-- Camera -->
      <h4 class="settings-field__subtitle">{{ $t('settings.avatar.camera') }}</h4>
      <div class="settings-field">
        <label class="settings-field__label" for="avatar-camDist">
          {{ $t('settings.avatar.cameraDistance') }}: {{ cameraDistance.toFixed(2) }}
        </label>
        <input
          id="avatar-camDist"
          v-model.number="cameraDistance"
          type="range"
          min="0.5"
          max="4"
          step="0.1"
          class="settings-field__range"
        >
      </div>
      <div class="settings-field">
        <label class="settings-field__label" for="avatar-camH">
          {{ $t('settings.avatar.cameraHeight') }}: {{ cameraHeight.toFixed(2) }}
        </label>
        <input
          id="avatar-camH"
          v-model.number="cameraHeight"
          type="range"
          min="0.5"
          max="2"
          step="0.05"
          class="settings-field__range"
        >
      </div>
      <div class="settings-field">
        <label class="settings-field__label" for="avatar-fov">
          {{ $t('settings.avatar.cameraFov') }}: {{ cameraFov }}°
        </label>
        <input
          id="avatar-fov"
          v-model.number="cameraFov"
          type="range"
          min="20"
          max="90"
          step="1"
          class="settings-field__range"
        >
      </div>

      <!-- Lighting -->
      <h4 class="settings-field__subtitle">{{ $t('settings.avatar.lighting') }}</h4>
      <div class="settings-field">
        <label class="settings-field__label" for="avatar-ambient">
          {{ $t('settings.avatar.ambientIntensity') }}: {{ ambientIntensity.toFixed(2) }}
        </label>
        <input
          id="avatar-ambient"
          v-model.number="ambientIntensity"
          type="range"
          min="0"
          max="2"
          step="0.05"
          class="settings-field__range"
        >
      </div>
      <div class="settings-field">
        <label class="settings-field__label" for="avatar-directional">
          {{ $t('settings.avatar.directionalIntensity') }}: {{ directionalIntensity.toFixed(2) }}
        </label>
        <input
          id="avatar-directional"
          v-model.number="directionalIntensity"
          type="range"
          min="0"
          max="2"
          step="0.05"
          class="settings-field__range"
        >
      </div>

      <!-- Eye tracking -->
      <h4 class="settings-field__subtitle">{{ $t('settings.avatar.eyeTracking') }}</h4>
      <div class="settings-field">
        <div class="settings-field__chip-group" role="radiogroup">
          <button
            v-for="m in eyeModes"
            :key="m.value"
            type="button"
            role="radio"
            :aria-checked="eyeTrackingMode === m.value"
            :class="['settings-field__chip', { 'settings-field__chip--active': eyeTrackingMode === m.value }]"
            @click="eyeTrackingMode = m.value"
          >
            {{ $t(m.labelKey) }}
          </button>
        </div>
        <p class="settings-field__hint">{{ $t('settings.avatar.eyeHint') }}</p>
      </div>

      <!-- Outline -->
      <h4 class="settings-field__subtitle">{{ $t('settings.avatar.outline') }}</h4>
      <div class="settings-field">
        <label class="settings-field__label">
          <input v-model="outlineEnabled" type="checkbox">
          {{ $t('settings.avatar.outlineEnabled') }}
        </label>
      </div>
      <template v-if="outlineEnabled">
        <div class="settings-field">
          <label class="settings-field__label" for="avatar-outline-thickness">
            {{ $t('settings.avatar.outlineThickness') }}: {{ outlineThickness.toFixed(4) }}
          </label>
          <input
            id="avatar-outline-thickness"
            v-model.number="outlineThickness"
            type="range"
            min="0"
            max="0.01"
            step="0.0005"
            class="settings-field__range"
          >
        </div>
        <div class="settings-field">
          <label class="settings-field__label" for="avatar-outline-color">
            {{ $t('settings.avatar.outlineColor') }}
          </label>
          <div class="settings-field__row">
            <input
              id="avatar-outline-color"
              v-model="outlineColor"
              type="color"
              class="settings-field__color"
            >
            <code class="settings-field__code">{{ outlineColor }}</code>
          </div>
        </div>
        <div class="settings-field">
          <label class="settings-field__label" for="avatar-outline-alpha">
            {{ $t('settings.avatar.outlineAlpha') }}: {{ outlineAlpha.toFixed(2) }}
          </label>
          <input
            id="avatar-outline-alpha"
            v-model.number="outlineAlpha"
            type="range"
            min="0"
            max="1"
            step="0.05"
            class="settings-field__range"
          >
        </div>
      </template>
    </template>

    <div class="settings-section__actions">
      <button type="button" class="settings-section__btn" @click="store.reset()">
        {{ $t('settings.common.reset') }}
      </button>
    </div>
  </section>
</template>

<style scoped>
@import './section-common.css';

.settings-field__subtitle {
  margin: 0.75rem 0 0.25rem 0;
  font-size: 0.78rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--airi-text-muted);
  opacity: 0.8;
}

.settings-field__color {
  width: 48px;
  height: 32px;
  padding: 0;
  border: 1px solid oklch(0.74 0.127 var(--seren-hue) / 0.2);
  border-radius: 6px;
  background: transparent;
  cursor: pointer;
}

.settings-field__code {
  font-size: 0.78rem;
  color: var(--airi-text-muted);
  font-family: ui-monospace, monospace;
}
</style>
