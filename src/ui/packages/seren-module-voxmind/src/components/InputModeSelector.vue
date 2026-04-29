<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import type { VoiceInputMode } from '@seren/ui-shared'

defineProps<{ modelValue: VoiceInputMode }>()
const emit = defineEmits<{ (e: 'update:modelValue', value: VoiceInputMode): void }>()

const { t } = useI18n()

function pick(mode: VoiceInputMode): void {
  emit('update:modelValue', mode)
}
</script>

<template>
  <div class="settings-field">
    <label class="settings-field__label">
      {{ t('modules.voxmind.inputMode.label') }}
    </label>
    <div class="input-mode" role="radiogroup" :aria-label="t('modules.voxmind.inputMode.label')">
      <button
        type="button"
        class="input-mode__btn"
        :class="{ 'input-mode__btn--active': modelValue === 'vad' }"
        role="radio"
        :aria-checked="modelValue === 'vad'"
        @click="pick('vad')"
      >
        {{ t('modules.voxmind.inputMode.vad') }}
      </button>
      <button
        type="button"
        class="input-mode__btn"
        :class="{ 'input-mode__btn--active': modelValue === 'ptt' }"
        role="radio"
        :aria-checked="modelValue === 'ptt'"
        @click="pick('ptt')"
      >
        {{ t('modules.voxmind.inputMode.ptt') }}
      </button>
    </div>
    <p class="settings-field__hint">
      {{ modelValue === 'ptt'
        ? t('modules.voxmind.inputMode.pttHint')
        : t('modules.voxmind.inputMode.vadHint')
      }}
    </p>
  </div>
</template>

<style scoped>
.input-mode {
  display: inline-flex;
  border-radius: 8px;
  border: 1px solid oklch(0.74 0.127 var(--seren-hue) / 0.2);
  overflow: hidden;
  background: var(--airi-input-tint);
}

.input-mode__btn {
  flex: 1 1 50%;
  padding: 0.45rem 0.9rem;
  border: none;
  background: transparent;
  color: var(--airi-text-muted);
  cursor: pointer;
  font-size: 0.82rem;
  font-family: inherit;
  transition: background 0.12s, color 0.12s;
}

.input-mode__btn + .input-mode__btn {
  border-left: 1px solid oklch(0.74 0.127 var(--seren-hue) / 0.2);
}

.input-mode__btn:hover:not(.input-mode__btn--active) {
  background: oklch(0.74 0.127 var(--seren-hue) / 0.1);
  color: var(--airi-text);
}

.input-mode__btn--active {
  background: oklch(0.74 0.127 var(--seren-hue) / 0.85);
  color: white;
}
</style>
