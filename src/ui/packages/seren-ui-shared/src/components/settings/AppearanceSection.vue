<script setup lang="ts">
import { computed } from 'vue'
import { storeToRefs } from 'pinia'
import { useAppearanceSettingsStore, type ThemeMode } from '../../stores/settings/appearance'

const store = useAppearanceSettingsStore()
const { themeMode, primaryHue, locale } = storeToRefs(store)

const themeModes: Array<{ value: ThemeMode, labelKey: string }> = [
  { value: 'auto', labelKey: 'settings.appearance.themeAuto' },
  { value: 'light', labelKey: 'settings.appearance.themeLight' },
  { value: 'dark', labelKey: 'settings.appearance.themeDark' },
]

function setThemeMode(m: ThemeMode): void {
  themeMode.value = m
}

const swatchStyle = computed(() => ({
  background: `oklch(0.74 0.127 ${primaryHue.value})`,
}))
</script>

<template>
  <section class="settings-section">
    <h3 class="settings-section__title">{{ $t('settings.appearance.title') }}</h3>

    <div class="settings-field">
      <span class="settings-field__label">{{ $t('settings.appearance.themeMode') }}</span>
      <div class="settings-field__chip-group" role="radiogroup">
        <button
          v-for="t in themeModes"
          :key="t.value"
          type="button"
          role="radio"
          :aria-checked="themeMode === t.value"
          :class="['settings-field__chip', { 'settings-field__chip--active': themeMode === t.value }]"
          @click="setThemeMode(t.value)"
        >
          {{ $t(t.labelKey) }}
        </button>
      </div>
    </div>

    <div class="settings-field">
      <label class="settings-field__label" for="appearance-hue">
        {{ $t('settings.appearance.primaryHue') }}: {{ primaryHue }}°
      </label>
      <div class="settings-field__row">
        <input
          id="appearance-hue"
          v-model.number="primaryHue"
          type="range"
          min="0"
          max="360"
          step="1"
          class="settings-field__range"
        >
        <span class="settings-field__swatch" :style="swatchStyle" aria-hidden="true" />
      </div>
      <p class="settings-field__hint">{{ $t('settings.appearance.primaryHueHint') }}</p>
    </div>

    <div class="settings-field">
      <label class="settings-field__label" for="appearance-locale">
        {{ $t('settings.appearance.locale') }}
      </label>
      <select id="appearance-locale" v-model="locale" class="settings-field__select">
        <option value="fr">Français</option>
        <option value="en">English</option>
      </select>
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
