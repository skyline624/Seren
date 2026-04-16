<script setup lang="ts">
import { BarcodeScanner } from '@capacitor-mlkit/barcode-scanning'
import { Capacitor } from '@capacitor/core'
import { useI18n } from 'vue-i18n'
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { parseConnectionQr, useConnectionStore } from '../stores/connection'

const router = useRouter()
const connectionStore = useConnectionStore()
const { t } = useI18n()

const isScanning = ref(false)
const manualInput = ref('')
const errorMessage = ref('')

async function scanQr(): Promise<void> {
  errorMessage.value = ''
  if (!Capacitor.isNativePlatform()) {
    errorMessage.value = 'QR scanning is only available on native builds. Use the manual input below.'
    return
  }

  isScanning.value = true
  try {
    const permission = await BarcodeScanner.requestPermissions()
    if (permission.camera !== 'granted') {
      errorMessage.value = 'Camera permission denied.'
      return
    }

    const result = await BarcodeScanner.scan()
    const first = result.barcodes[0]
    if (!first?.rawValue) {
      errorMessage.value = 'No QR code detected.'
      return
    }

    await connectionStore.setFromQr(first.rawValue)
    await router.replace('/')
  }
  catch (err) {
    errorMessage.value = err instanceof Error ? err.message : 'Unknown scan error'
  }
  finally {
    isScanning.value = false
  }
}

async function submitManual(): Promise<void> {
  errorMessage.value = ''
  try {
    parseConnectionQr(manualInput.value)
    await connectionStore.setFromQr(manualInput.value)
    await router.replace('/')
  }
  catch (err) {
    errorMessage.value = err instanceof Error ? err.message : 'Invalid URL'
  }
}
</script>

<template>
  <div class="onboarding">
    <header class="onboarding-header">
      <h2>{{ t('app.name') }}</h2>
      <p>{{ t('app.tagline') }}</p>
    </header>

    <section class="onboarding-card">
      <p class="hint">
        Scan the QR code shown by your Seren hub to connect this device.
      </p>
      <button class="primary" :disabled="isScanning" @click="scanQr">
        {{ isScanning ? t('audio.listening') : 'Scan QR code' }}
      </button>
    </section>

    <section class="onboarding-card">
      <label for="manual-url">Or paste the connect URL:</label>
      <input
        id="manual-url"
        v-model="manualInput"
        type="url"
        placeholder="seren://connect?ws=wss://...&token=..."
        autocomplete="off"
        autocapitalize="off"
      >
      <button class="secondary" @click="submitManual">
        Connect manually
      </button>
    </section>

    <p v-if="errorMessage" class="error">
      {{ errorMessage }}
    </p>
  </div>
</template>

<style scoped>
.onboarding {
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
  padding: 1.5rem;
  max-width: 28rem;
  margin: 0 auto;
}
.onboarding-header h2 {
  font-size: 1.5rem;
  font-weight: 700;
  color: #0f172a;
}
.onboarding-header p {
  color: #64748b;
  margin-top: 0.25rem;
}
.onboarding-card {
  background: #fff;
  border: 1px solid #e2e8f0;
  border-radius: 0.75rem;
  padding: 1rem;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}
.hint {
  color: #475569;
  font-size: 0.875rem;
}
label {
  font-size: 0.8125rem;
  color: #475569;
}
input {
  padding: 0.625rem 0.75rem;
  border-radius: 0.5rem;
  border: 1px solid #cbd5e1;
  font-size: 0.875rem;
}
button {
  padding: 0.75rem 1rem;
  border-radius: 0.5rem;
  font-weight: 600;
  font-size: 0.9375rem;
  cursor: pointer;
  border: none;
}
.primary {
  background: #2563eb;
  color: #fff;
}
.primary:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
.secondary {
  background: #f1f5f9;
  color: #0f172a;
}
.error {
  color: #dc2626;
  font-size: 0.875rem;
  text-align: center;
}
</style>
