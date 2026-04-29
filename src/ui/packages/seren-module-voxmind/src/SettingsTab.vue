<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { storeToRefs } from 'pinia'
import { useI18n } from 'vue-i18n'
import {
  SILERO_FRAME_MS,
  useVoiceSettingsStore,
} from '@seren/ui-shared'
import ModelRow from './components/ModelRow.vue'
import VuMeter from './components/VuMeter.vue'
import { useVoxMindSettingsStore } from './stores/voxmind'

const store = useVoxMindSettingsStore()
const { models, sttEngine, downloadedModels, error, loading } = storeToRefs(store)

// VAD + language settings are owned by the voice store in `@seren/ui-shared`.
// The audio module that originally hosted the VAD threshold has been folded
// into this tab — VAD + STT belong to the same conceptual flow.
const voiceStore = useVoiceSettingsStore()
const {
  vadThreshold,
  negativeSpeechThreshold,
  redemptionFrames,
  sttLanguage,
} = storeToRefs(voiceStore)

const { t } = useI18n()

// ── Live VU-meter calibration (opt-in: requires mic permission) ─────────────
//
// `@seren/ui-audio` is an optional peer dep — lite deployments that ship
// without VAD support gracefully degrade (the calibration block is
// hidden, see `isVoiceModuleAvailable`). The `as string` cast on the
// import path mirrors the pattern in ChatPanel — it bypasses the static
// resolution so a missing peer doesn't break the type-check.
interface VoiceInputApi {
  start: () => Promise<void>
  stop: () => void
}
interface VoiceInputModuleShape {
  useVoiceInput: (opts: {
    threshold?: number
    negativeSpeechThreshold?: number
    redemptionFrames?: number
    onFrameProgress?: (probability: number) => void
  }) => VoiceInputApi
}

const calibrationLevel = ref(0)
const isCalibrating = ref(false)
const calibrationError = ref<string | null>(null)
let calibrationStop: (() => void) | null = null

async function toggleCalibration(): Promise<void> {
  if (isCalibrating.value) {
    calibrationStop?.()
    calibrationStop = null
    isCalibrating.value = false
    calibrationLevel.value = 0
    return
  }

  calibrationError.value = null
  try {
    const mod = (await import('@seren/ui-audio' as string)) as VoiceInputModuleShape
    const vad = mod.useVoiceInput({
      threshold: vadThreshold.value,
      negativeSpeechThreshold: negativeSpeechThreshold.value,
      redemptionFrames: redemptionFrames.value,
      onFrameProgress: (probability: number) => {
        calibrationLevel.value = probability
      },
    })

    await vad.start()
    calibrationStop = vad.stop
    isCalibrating.value = true
  }
  catch (e) {
    calibrationError.value = e instanceof Error ? e.message : String(e)
    isCalibrating.value = false
  }
}

// ── Slider bounds + display helpers (KISS — pure functions, no state) ───────

/** Negative threshold must stay strictly below positive — clamp the upper bound
 * dynamically so the user can't lock themselves out of `onSpeechEnd`. */
const negativeMax = computed(() => Math.max(0.05, vadThreshold.value - 0.05))

const redemptionMs = computed(() => redemptionFrames.value * SILERO_FRAME_MS)

const isVoiceModuleAvailable = ref(true)

// ── Lifecycle ───────────────────────────────────────────────────────────────
onMounted(() => {
  void store.refresh()
  // Probe @seren/ui-audio availability so we can hide the calibration UI on
  // lite deployments rather than render a button that always fails.
  import('@seren/ui-audio' as string)
    .then(() => { isVoiceModuleAvailable.value = true })
    .catch(() => { isVoiceModuleAvailable.value = false })
})

onUnmounted(() => {
  calibrationStop?.()
  calibrationStop = null
  // Note: store pollers persist across remounts so navigating away during
  // a download keeps the transfer alive.
})

function resetAll(): void {
  store.reset()
  voiceStore.reset()
}
</script>

<template>
  <section class="settings-section">
    <h3 class="settings-section__title">{{ t('modules.voxmind.title') }}</h3>
    <p class="settings-section__hint">{{ t('modules.voxmind.engine.hint') }}</p>

    <p v-if="error" class="settings-section__error" role="alert">
      {{ error }}
    </p>

    <ul
      class="model-list"
      role="radiogroup"
      :aria-label="t('modules.voxmind.engine.label')"
      :aria-busy="loading"
    >
      <ModelRow
        v-for="m in models"
        :key="m.id"
        :model="m"
        :is-active="sttEngine === m.id"
        :is-only-downloaded="downloadedModels.length === 1 && (m.isDownloaded || m.isSystemManaged)"
        @select="store.selectActive(m.id)"
        @download="store.download(m.id)"
        @delete="store.remove(m.id)"
      />
    </ul>

    <!-- Language ────────────────────────────────────────────────────── -->
    <h4 class="settings-section__subtitle">{{ t('modules.voxmind.language.label') }}</h4>
    <div class="settings-field">
      <select
        id="voxmind-stt-language"
        v-model="sttLanguage"
        class="settings-field__select"
      >
        <option value="auto">{{ t('modules.voxmind.language.auto') }}</option>
        <option value="fr">{{ t('modules.voxmind.language.fr') }}</option>
        <option value="en">{{ t('modules.voxmind.language.en') }}</option>
      </select>
      <p class="settings-field__hint">{{ t('modules.voxmind.language.hint') }}</p>
    </div>

    <!-- Mic sensitivity (VAD) ───────────────────────────────────────── -->
    <h4 class="settings-section__subtitle">{{ t('modules.voxmind.vad.title') }}</h4>

    <div class="settings-field">
      <label class="settings-field__label" for="voxmind-vad-threshold">
        {{ t('modules.voxmind.vad.threshold') }}: {{ vadThreshold.toFixed(2) }}
      </label>
      <input
        id="voxmind-vad-threshold"
        v-model.number="vadThreshold"
        type="range"
        min="0.1"
        max="0.95"
        step="0.05"
        class="settings-field__range"
      >
      <p class="settings-field__hint">{{ t('modules.voxmind.vad.thresholdHint') }}</p>
    </div>

    <div class="settings-field">
      <label class="settings-field__label" for="voxmind-vad-neg-threshold">
        {{ t('modules.voxmind.vad.negativeThreshold') }}: {{ negativeSpeechThreshold.toFixed(2) }}
      </label>
      <input
        id="voxmind-vad-neg-threshold"
        v-model.number="negativeSpeechThreshold"
        type="range"
        min="0.05"
        :max="negativeMax"
        step="0.05"
        class="settings-field__range"
      >
      <p class="settings-field__hint">{{ t('modules.voxmind.vad.negativeThresholdHint') }}</p>
    </div>

    <div class="settings-field">
      <label class="settings-field__label" for="voxmind-vad-redemption">
        {{ t('modules.voxmind.vad.redemption') }}: {{ redemptionMs }} ms
      </label>
      <input
        id="voxmind-vad-redemption"
        v-model.number="redemptionFrames"
        type="range"
        min="10"
        max="60"
        step="2"
        class="settings-field__range"
      >
      <p class="settings-field__hint">{{ t('modules.voxmind.vad.redemptionHint') }}</p>
    </div>

    <!-- Live VU-meter (opt-in calibration) ──────────────────────────── -->
    <div v-if="isVoiceModuleAvailable" class="settings-field">
      <label class="settings-field__label">{{ t('modules.voxmind.vad.level') }}</label>
      <VuMeter
        :level="calibrationLevel"
        :positive-threshold="vadThreshold"
        :negative-threshold="negativeSpeechThreshold"
      />
      <button
        type="button"
        class="settings-section__btn settings-section__btn--inline"
        @click="toggleCalibration"
      >
        {{ isCalibrating ? t('modules.voxmind.vad.stopTest') : t('modules.voxmind.vad.startTest') }}
      </button>
      <p v-if="calibrationError" class="settings-section__error" role="alert">
        {{ calibrationError }}
      </p>
    </div>

    <div class="settings-section__actions">
      <button type="button" class="settings-section__btn" @click="store.refresh()">
        {{ t('settings.common.refresh') }}
      </button>
      <button type="button" class="settings-section__btn" @click="resetAll">
        {{ t('settings.common.reset') }}
      </button>
    </div>
  </section>
</template>

<style scoped>
.settings-section {
  display: flex;
  flex-direction: column;
  gap: 0.85rem;
}

.settings-section__title {
  font-size: 1rem;
  font-weight: 600;
  margin: 0 0 0.1rem 0;
  color: var(--airi-text);
}

.settings-section__subtitle {
  font-size: 0.86rem;
  font-weight: 600;
  margin: 0.6rem 0 0.1rem 0;
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

.settings-field__range {
  width: 100%;
  cursor: pointer;
  accent-color: var(--airi-accent);
}

.settings-field__select {
  width: 100%;
  padding: 0.45rem 0.7rem;
  border-radius: 8px;
  border: 1px solid oklch(0.74 0.127 var(--seren-hue) / 0.2);
  background: var(--airi-input-tint);
  color: var(--airi-text);
  font-size: 0.85rem;
  font-family: inherit;
  cursor: pointer;
}

.settings-field__hint {
  margin: 0;
  font-size: 0.72rem;
  color: var(--airi-text-muted);
  opacity: 0.75;
}

.settings-section__hint {
  margin: 0;
  font-size: 0.78rem;
  color: var(--airi-text-muted);
  line-height: 1.45;
}

.settings-section__error {
  margin: 0;
  padding: 0.5rem 0.75rem;
  border-radius: 8px;
  background: oklch(0.62 0.18 25 / 0.1);
  color: oklch(0.55 0.18 25);
  font-size: 0.78rem;
}

.model-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
}

.settings-section__actions {
  display: flex;
  gap: 0.5rem;
  margin-top: 0.25rem;
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

.settings-section__btn--inline {
  align-self: flex-start;
}
</style>
