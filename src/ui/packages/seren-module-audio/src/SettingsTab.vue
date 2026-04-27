<script setup lang="ts">
import { useVoiceSettingsStore } from '@seren/ui-shared'
import { storeToRefs } from 'pinia'
import { useI18n } from 'vue-i18n'

const store = useVoiceSettingsStore()
const { vadThreshold } = storeToRefs(store)
const { t } = useI18n()
</script>

<template>
  <section class="settings-section">
    <h3 class="settings-section__title">{{ t('modules.audio.title') }}</h3>

    <div class="settings-field">
      <label class="settings-field__label" for="audio-vad">
        {{ t('modules.audio.vadThreshold') }}: {{ vadThreshold.toFixed(2) }}
      </label>
      <input
        id="audio-vad"
        v-model.number="vadThreshold"
        type="range"
        min="0.1"
        max="0.95"
        step="0.05"
        class="settings-field__range"
      >
      <p class="settings-field__hint">{{ t('modules.audio.vadThresholdHint') }}</p>
    </div>

    <div class="settings-section__actions">
      <button type="button" class="settings-section__btn" @click="store.reset()">
        {{ t('settings.common.reset') }}
      </button>
    </div>
  </section>
</template>

<style scoped>
/* Use the same shared section styles as the in-tree settings sections.
 * Stylesheet lives in @seren/ui-shared so every module + core section
 * stays in lockstep. */
.settings-section {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.settings-section__title {
  font-size: 1rem;
  font-weight: 600;
  margin: 0 0 0.25rem 0;
  color: var(--airi-text);
}

.settings-field {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
}

.settings-field__label {
  font-size: 0.8rem;
  font-weight: 500;
  color: var(--airi-text-muted);
}

.settings-field__hint {
  margin: 0;
  font-size: 0.72rem;
  color: var(--airi-text-muted);
  opacity: 0.75;
}

.settings-field__range {
  width: 100%;
  cursor: pointer;
  accent-color: var(--airi-accent);
}

.settings-section__actions {
  display: flex;
  gap: 0.5rem;
  margin-top: 0.5rem;
}

.settings-section__btn {
  padding: 0.45rem 0.9rem;
  border: 1px solid oklch(0.74 0.127 var(--seren-hue) / 0.2);
  border-radius: 8px;
  background: var(--airi-input-tint);
  color: var(--airi-text-muted);
  cursor: pointer;
  font-size: 0.8rem;
  font-family: inherit;
  transition: background 0.15s, color 0.15s;
}

.settings-section__btn:hover {
  background: oklch(0.74 0.127 var(--seren-hue) / 0.22);
  color: var(--airi-text);
}
</style>
