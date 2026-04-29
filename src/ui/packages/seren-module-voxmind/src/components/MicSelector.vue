<script setup lang="ts">
import { computed, onMounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{
  /** Current persisted device id (or `'default'` for system pick). */
  modelValue: string
  /** Available audio inputs (kind === 'audioinput'). */
  devices: ReadonlyArray<MediaDeviceInfo>
  /** True while the underlying enumeration is in flight. */
  isLoading: boolean
  /** True when the browser exposed labels (i.e. mic permission granted). */
  hasPermission: boolean
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', value: string): void
  (e: 'refresh'): void
}>()

const { t } = useI18n()

/** Sentinel used in the persisted store for "OS-default mic". */
const DEFAULT_VALUE = 'default'

const selectedValue = computed({
  get: () => props.modelValue || DEFAULT_VALUE,
  set: (v: string) => emit('update:modelValue', v),
})

/**
 * If the persisted device id no longer matches an available device
 * (the headset was unplugged), silently fall back to <c>'default'</c>
 * so the user keeps a working mic — the next refresh will surface the
 * canonical list.
 */
watch(
  () => props.devices.map(d => d.deviceId),
  (ids) => {
    if (
      props.modelValue
      && props.modelValue !== DEFAULT_VALUE
      && ids.length > 0
      && !ids.includes(props.modelValue)
    ) {
      emit('update:modelValue', DEFAULT_VALUE)
    }
  },
)

onMounted(() => {
  // Trigger a first enumeration pass on mount in case the parent
  // didn't already.
  emit('refresh')
})

function deviceLabel(d: MediaDeviceInfo): string {
  if (d.label) {
    return d.label
  }
  // Browser hides labels until permission granted — fall back to a
  // truncated id that's still unique-looking.
  return t('modules.voxmind.mic.unlabeled', { id: d.deviceId.slice(0, 8) })
}
</script>

<template>
  <div class="settings-field">
    <label class="settings-field__label" for="voxmind-mic-select">
      {{ t('modules.voxmind.mic.label') }}
    </label>
    <div class="mic-selector__row">
      <select
        id="voxmind-mic-select"
        v-model="selectedValue"
        class="settings-field__select"
        :disabled="isLoading"
      >
        <option value="default">{{ t('modules.voxmind.mic.systemDefault') }}</option>
        <option
          v-for="d in devices"
          :key="d.deviceId"
          :value="d.deviceId"
        >
          {{ deviceLabel(d) }}
        </option>
      </select>
      <button
        type="button"
        class="mic-selector__refresh"
        :disabled="isLoading"
        :title="t('modules.voxmind.mic.refresh')"
        :aria-label="t('modules.voxmind.mic.refresh')"
        @click="emit('refresh')"
      >
        <svg viewBox="0 0 16 16" width="14" height="14" aria-hidden="true">
          <path
            d="M2.5 8a5.5 5.5 0 0 1 9.7-3.5M13.5 8a5.5 5.5 0 0 1-9.7 3.5M12 2v3h-3M4 14v-3h3"
            fill="none"
            stroke="currentColor"
            stroke-width="1.4"
            stroke-linecap="round"
            stroke-linejoin="round"
          />
        </svg>
      </button>
    </div>
    <p v-if="!hasPermission" class="settings-field__hint settings-field__hint--warn">
      {{ t('modules.voxmind.mic.permissionHint') }}
    </p>
    <p v-else class="settings-field__hint">
      {{ t('modules.voxmind.mic.hint') }}
    </p>
  </div>
</template>

<style scoped>
.mic-selector__row {
  display: flex;
  gap: 0.5rem;
  align-items: center;
}

.mic-selector__refresh {
  flex: 0 0 auto;
  width: 32px;
  height: 32px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: 8px;
  border: 1px solid oklch(0.74 0.127 var(--seren-hue) / 0.2);
  background: var(--airi-input-tint);
  color: var(--airi-text-muted);
  cursor: pointer;
  transition: background 0.12s, color 0.12s;
}

.mic-selector__refresh:hover:not(:disabled) {
  background: oklch(0.74 0.127 var(--seren-hue) / 0.18);
  color: var(--airi-text);
}

.mic-selector__refresh:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.settings-field__hint--warn {
  color: oklch(0.7 0.13 65);
}
</style>
