<script setup lang="ts">
import { storeToRefs } from 'pinia'
import { useConnectionSettingsStore } from '../../stores/settings/connection'

const store = useConnectionSettingsStore()
const { serverUrl, token } = storeToRefs(store)
</script>

<template>
  <section class="settings-section">
    <h3 class="settings-section__title">{{ $t('settings.connection.title') }}</h3>

    <div class="settings-field">
      <label class="settings-field__label" for="conn-server-url">
        {{ $t('settings.connection.serverUrl') }}
      </label>
      <input
        id="conn-server-url"
        v-model="serverUrl"
        type="text"
        placeholder="ws://localhost:5080/ws"
        class="settings-field__input"
      >
      <p class="settings-field__hint">{{ $t('settings.connection.serverUrlHint') }}</p>
    </div>

    <div class="settings-field">
      <label class="settings-field__label" for="conn-token">
        {{ $t('settings.connection.authToken') }}
      </label>
      <input
        id="conn-token"
        v-model="token"
        type="password"
        placeholder="JWT (optional in dev)"
        class="settings-field__input"
      >
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
