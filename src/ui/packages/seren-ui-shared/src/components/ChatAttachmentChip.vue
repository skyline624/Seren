<script setup lang="ts">
import { computed } from 'vue'
import type { PendingAttachment } from '../composables/useAttachmentValidation'
import { formatByteSize } from '../composables/useAttachmentValidation'
import { isImageMimeType } from '../composables/useAttachmentConstraints'

const props = defineProps<{
  attachment: PendingAttachment
  removable?: boolean
}>()

const emit = defineEmits<{
  (e: 'remove', id: string): void
}>()

const isImage = computed(() => isImageMimeType(props.attachment.mimeType))
const hasThumbnail = computed(() => isImage.value && !!props.attachment.previewUrl)

function handleRemove(): void {
  emit('remove', props.attachment.id)
}
</script>

<template>
  <div class="attachment-chip" :class="{ 'attachment-chip--image': isImage }">
    <img
      v-if="hasThumbnail"
      class="attachment-chip__thumb"
      :src="attachment.previewUrl!"
      :alt="attachment.fileName"
    >
    <div v-else class="attachment-chip__icon">
      {{ isImage ? '📷' : '📎' }}
    </div>
    <div class="attachment-chip__meta">
      <span class="attachment-chip__name">{{ attachment.fileName }}</span>
      <span class="attachment-chip__size">{{ formatByteSize(attachment.byteSize) }}</span>
    </div>
    <button
      v-if="removable !== false"
      type="button"
      class="attachment-chip__remove"
      aria-label="Remove attachment"
      @click="handleRemove"
    >
      ×
    </button>
  </div>
</template>

<style scoped>
.attachment-chip {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.35rem 0.55rem 0.35rem 0.4rem;
  border: 1px solid rgba(100, 180, 200, 0.25);
  border-radius: 10px;
  background: rgba(15, 23, 42, 0.55);
  color: #e2e8f0;
  font-size: 0.75rem;
  max-width: 240px;
  position: relative;
}

.attachment-chip--image {
  padding-left: 0.3rem;
}

.attachment-chip__thumb {
  width: 40px;
  height: 40px;
  border-radius: 6px;
  object-fit: cover;
  flex-shrink: 0;
}

.attachment-chip__icon {
  width: 40px;
  height: 40px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 6px;
  background: rgba(100, 180, 200, 0.12);
  font-size: 1.1rem;
  flex-shrink: 0;
}

.attachment-chip__meta {
  display: flex;
  flex-direction: column;
  min-width: 0;
  flex: 1;
}

.attachment-chip__name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-weight: 500;
}

.attachment-chip__size {
  color: #94a3b8;
  font-size: 0.7rem;
}

.attachment-chip__remove {
  background: rgba(239, 68, 68, 0.15);
  color: #fca5a5;
  border: none;
  border-radius: 50%;
  width: 18px;
  height: 18px;
  font-size: 0.9rem;
  line-height: 1;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  padding: 0;
}

.attachment-chip__remove:hover {
  background: rgba(239, 68, 68, 0.3);
  color: #fff;
}
</style>
