<script setup lang="ts">
defineProps<{
  modelValue: string
  sections: Array<{ id: string, labelKey: string, icon: string }>
}>()

defineEmits<{
  'update:modelValue': [value: string]
}>()
</script>

<template>
  <nav class="section-nav" aria-label="Settings sections">
    <button
      v-for="s in sections"
      :key="s.id"
      type="button"
      :class="['section-nav__item', { 'section-nav__item--active': modelValue === s.id }]"
      @click="$emit('update:modelValue', s.id)"
    >
      <span class="section-nav__icon" v-html="s.icon" />
      <span class="section-nav__label">{{ $t(s.labelKey) }}</span>
    </button>
  </nav>
</template>

<style scoped>
.section-nav {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
  padding: 0.25rem 0;
  min-width: 160px;
  flex-shrink: 0;
}

.section-nav__item {
  display: flex;
  align-items: center;
  gap: 0.625rem;
  padding: 0.5rem 0.75rem;
  border: none;
  background: transparent;
  border-radius: 8px;
  color: var(--airi-text-muted);
  cursor: pointer;
  font: inherit;
  font-size: 0.875rem;
  text-align: left;
  transition: background 0.15s, color 0.15s;
}

.section-nav__item:hover {
  background: var(--airi-input-tint);
  color: var(--airi-text);
}

.section-nav__item--active {
  background: var(--airi-input-tint);
  color: var(--airi-text);
  font-weight: 600;
}

.section-nav__icon {
  display: inline-flex;
  align-items: center;
  width: 16px;
  height: 16px;
}

.section-nav__icon :deep(svg) {
  width: 100%;
  height: 100%;
  fill: currentColor;
}

.section-nav__label {
  flex: 1;
}
</style>
