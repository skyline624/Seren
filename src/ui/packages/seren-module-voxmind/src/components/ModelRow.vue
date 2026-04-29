<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { VoxMindModel } from '../api/modelsClient'
import ProgressRing from './ProgressRing.vue'

const props = defineProps<{
  model: VoxMindModel
  isActive: boolean
  /**
   * When true, this is the only downloaded variant — hides the delete
   * button to enforce the "≥ 1 engine must remain" invariant on the
   * client side. The server enforces the same rule with a 409.
   */
  isOnlyDownloaded: boolean
}>()

const emit = defineEmits<{
  (e: 'select'): void
  (e: 'download'): void
  (e: 'delete'): void
}>()

const { t } = useI18n()

const downloading = computed(() => props.model.download?.status === 'downloading')
const failed = computed(() => props.model.download?.status === 'failed')
const selectable = computed(() => props.model.isDownloaded || props.model.isSystemManaged)

const sizeLabel = computed(() => `~${props.model.approxSizeMb} MB`)
const nameKey = computed(() => `modules.voxmind.models.${props.model.displayKey}.name`)
const showDelete = computed(() =>
  props.model.isDownloaded
  && !props.model.isSystemManaged
  && !props.isOnlyDownloaded,
)

function onRowClick() {
  if (selectable.value) {
    emit('select')
  }
}

function onDownloadClick(event: MouseEvent) {
  event.stopPropagation()
  emit('download')
}

function onDeleteClick(event: MouseEvent) {
  event.stopPropagation()
  if (window.confirm(t('modules.voxmind.actions.deleteConfirm', { name: t(nameKey.value) }))) {
    emit('delete')
  }
}
</script>

<template>
  <li
    class="model-row"
    :class="{
      'model-row--active': isActive,
      'model-row--selectable': selectable,
      'model-row--unavailable': !selectable && !downloading,
      'model-row--downloading': downloading,
      'model-row--failed': failed,
    }"
    :role="selectable ? 'radio' : 'presentation'"
    :aria-checked="selectable ? isActive : undefined"
    :tabindex="selectable ? 0 : -1"
    @click="onRowClick"
    @keydown.enter.prevent="onRowClick"
    @keydown.space.prevent="onRowClick"
  >
    <span class="model-row__check" aria-hidden="true">
      <svg v-if="isActive" viewBox="0 0 16 16" width="14" height="14">
        <path d="M3 8.5l3 3 7-7" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" />
      </svg>
    </span>

    <div class="model-row__main">
      <span class="model-row__name">{{ t(nameKey) }}</span>
      <span class="model-row__meta">
        <span class="model-row__size">{{ sizeLabel }}</span>
        <span v-if="model.isSystemManaged" class="model-row__badge">
          {{ t('modules.voxmind.actions.systemManaged') }}
        </span>
        <span v-else-if="failed" class="model-row__error" :title="model.download?.error ?? ''">
          {{ t('modules.voxmind.errors.downloadFailed', { reason: model.download?.error ?? '' }) }}
        </span>
      </span>
    </div>

    <span class="model-row__action">
      <ProgressRing
        v-if="downloading"
        :done="model.download?.bytesDone ?? 0"
        :total="model.download?.bytesTotal ?? 0"
        :title="t('modules.voxmind.actions.downloading')"
      />
      <button
        v-else-if="!model.isDownloaded && !model.isSystemManaged"
        type="button"
        class="model-row__btn model-row__btn--download"
        :title="t('modules.voxmind.actions.download')"
        :aria-label="t('modules.voxmind.actions.download')"
        @click="onDownloadClick"
      >
        <svg viewBox="0 0 16 16" width="14" height="14" aria-hidden="true">
          <path d="M8 2v9m0 0l-3.5-3.5M8 11l3.5-3.5M3 13.5h10" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" />
        </svg>
      </button>
      <button
        v-else-if="showDelete"
        type="button"
        class="model-row__btn model-row__btn--delete"
        :title="t('modules.voxmind.actions.delete')"
        :aria-label="t('modules.voxmind.actions.delete')"
        @click="onDeleteClick"
      >
        <svg viewBox="0 0 16 16" width="14" height="14" aria-hidden="true">
          <path d="M4 4l8 8M12 4l-8 8" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" />
        </svg>
      </button>
    </span>
  </li>
</template>

<style scoped>
.model-row {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  padding: 0.55rem 0.75rem;
  border-radius: 10px;
  border: 1px solid oklch(0.74 0.127 var(--seren-hue) / 0.12);
  background: oklch(0.74 0.127 var(--seren-hue) / 0.04);
  cursor: default;
  transition: background 0.12s, border-color 0.12s, transform 0.06s;
}

.model-row + .model-row {
  margin-top: 0.4rem;
}

.model-row--selectable {
  cursor: pointer;
}

.model-row--selectable:hover,
.model-row--selectable:focus-visible {
  border-color: oklch(0.74 0.127 var(--seren-hue) / 0.45);
  background: oklch(0.74 0.127 var(--seren-hue) / 0.08);
  outline: none;
}

.model-row--unavailable {
  opacity: 0.7;
}

.model-row--active {
  border-color: oklch(0.74 0.127 var(--seren-hue) / 0.65);
  background: oklch(0.74 0.127 var(--seren-hue) / 0.12);
}

.model-row--failed {
  border-color: oklch(0.62 0.18 25 / 0.55);
}

.model-row__check {
  width: 18px;
  height: 18px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: 50%;
  border: 1.5px solid oklch(0.74 0.127 var(--seren-hue) / 0.3);
  color: oklch(0.74 0.127 var(--seren-hue));
  flex: 0 0 auto;
}

.model-row--active .model-row__check {
  background: oklch(0.74 0.127 var(--seren-hue) / 0.85);
  border-color: oklch(0.74 0.127 var(--seren-hue) / 0.85);
  color: white;
}

.model-row__main {
  flex: 1 1 auto;
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
  min-width: 0;
}

.model-row__name {
  font-size: 0.86rem;
  font-weight: 500;
  color: var(--airi-text);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.model-row__meta {
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem;
  align-items: center;
  font-size: 0.72rem;
  color: var(--airi-text-muted);
}

.model-row__badge {
  font-size: 0.66rem;
  padding: 0.05rem 0.4rem;
  border-radius: 999px;
  background: oklch(0.74 0.127 var(--seren-hue) / 0.18);
  color: var(--airi-text-muted);
}

.model-row__error {
  color: oklch(0.62 0.18 25);
  font-size: 0.72rem;
}

.model-row__action {
  flex: 0 0 auto;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 26px;
  height: 26px;
}

.model-row__btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 26px;
  height: 26px;
  border-radius: 7px;
  border: none;
  cursor: pointer;
  background: transparent;
  color: var(--airi-text-muted);
  transition: background 0.12s, color 0.12s;
}

.model-row__btn:hover {
  background: oklch(0.74 0.127 var(--seren-hue) / 0.18);
  color: var(--airi-text);
}

.model-row__btn--delete:hover {
  color: oklch(0.62 0.18 25);
  background: oklch(0.62 0.18 25 / 0.12);
}
</style>
