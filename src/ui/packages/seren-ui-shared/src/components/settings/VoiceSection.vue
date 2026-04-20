<script setup lang="ts">
import { storeToRefs } from 'pinia'
import { useVoiceSettingsStore } from '../../stores/settings/voice'

const store = useVoiceSettingsStore()
const { vadThreshold } = storeToRefs(store)
</script>

<template>
  <section class="settings-section">
    <h3 class="settings-section__title">{{ $t('settings.voice.title') }}</h3>

    <div class="settings-field">
      <label class="settings-field__label" for="voice-vad">
        {{ $t('settings.voice.vadThreshold') }}: {{ vadThreshold.toFixed(2) }}
      </label>
      <input
        id="voice-vad"
        v-model.number="vadThreshold"
        type="range"
        min="0.1"
        max="0.95"
        step="0.05"
        class="settings-field__range"
      >
      <p class="settings-field__hint">{{ $t('settings.voice.vadThresholdHint') }}</p>
    </div>

    <div class="settings-section__actions">
      <button type="button" class="settings-section__btn" @click="store.reset()">
        {{ $t('settings.common.reset') }}
      </button>
    </div>
  </section>
</template>

<style scoped>
@import './section-common.css';
</style>
