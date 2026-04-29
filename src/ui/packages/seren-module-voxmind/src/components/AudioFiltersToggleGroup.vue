<script setup lang="ts">
import { useI18n } from 'vue-i18n'

defineProps<{
  noiseSuppression: boolean
  echoCancellation: boolean
  autoGainControl: boolean
}>()

const emit = defineEmits<{
  (e: 'update:noiseSuppression', value: boolean): void
  (e: 'update:echoCancellation', value: boolean): void
  (e: 'update:autoGainControl', value: boolean): void
}>()

const { t } = useI18n()
</script>

<template>
  <div class="settings-field">
    <label class="settings-field__label">{{ t('modules.voxmind.filters.label') }}</label>
    <div class="filters">
      <label class="filters__row">
        <input
          type="checkbox"
          :checked="noiseSuppression"
          @change="emit('update:noiseSuppression', ($event.target as HTMLInputElement).checked)"
        >
        <span>{{ t('modules.voxmind.filters.noiseSuppression') }}</span>
      </label>
      <label class="filters__row">
        <input
          type="checkbox"
          :checked="echoCancellation"
          @change="emit('update:echoCancellation', ($event.target as HTMLInputElement).checked)"
        >
        <span>{{ t('modules.voxmind.filters.echoCancellation') }}</span>
      </label>
      <label class="filters__row">
        <input
          type="checkbox"
          :checked="autoGainControl"
          @change="emit('update:autoGainControl', ($event.target as HTMLInputElement).checked)"
        >
        <span>{{ t('modules.voxmind.filters.autoGainControl') }}</span>
      </label>
    </div>
    <p class="settings-field__hint">
      {{ t('modules.voxmind.filters.hint') }}
    </p>
  </div>
</template>

<style scoped>
.filters {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
  padding: 0.55rem 0.75rem;
  border-radius: 8px;
  border: 1px solid oklch(0.74 0.127 var(--seren-hue) / 0.18);
  background: oklch(0.74 0.127 var(--seren-hue) / 0.04);
}

.filters__row {
  display: flex;
  align-items: center;
  gap: 0.55rem;
  font-size: 0.82rem;
  color: var(--airi-text);
  cursor: pointer;
}

.filters__row input[type='checkbox'] {
  accent-color: var(--airi-accent);
  cursor: pointer;
}
</style>
