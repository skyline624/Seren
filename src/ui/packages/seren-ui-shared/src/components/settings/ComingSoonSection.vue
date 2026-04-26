<script setup lang="ts">
defineProps<{
  titleKey: string
  messageKey: string
}>()
</script>

<template>
  <section class="settings-section settings-section--coming-soon">
    <div class="coming-soon__icon" aria-hidden="true">
      <svg viewBox="0 0 24 24">
        <path d="M12 2a10 10 0 1 0 10 10A10 10 0 0 0 12 2Zm0 18a8 8 0 1 1 8-8 8 8 0 0 1-8 8Zm.5-13H11v6l5.25 3.15.75-1.23-4.5-2.67Z" />
      </svg>
    </div>
    <h3 class="settings-section__title coming-soon__title">{{ $t(titleKey) }}</h3>
    <p class="coming-soon__message">{{ $t(messageKey) }}</p>
  </section>
</template>

<style scoped>
@import './section-common.css';

.settings-section--coming-soon {
  align-items: center;
  text-align: center;
  padding: 2rem 1rem;
  gap: 0.75rem;
}

.coming-soon__icon {
  width: 48px;
  height: 48px;
  color: var(--airi-text-muted);
  opacity: 0.55;
}

.coming-soon__icon :deep(svg) {
  width: 100%;
  height: 100%;
  fill: currentColor;
}

.coming-soon__title {
  margin: 0;
}

.coming-soon__message {
  margin: 0;
  max-width: 32ch;
  font-size: 0.85rem;
  line-height: 1.45;
  color: var(--airi-text-muted);
}
</style>
