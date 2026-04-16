<script setup lang="ts">
import { useSettingsStore } from '../stores/settings'

const settings = useSettingsStore()
</script>

<template>
  <div class="settings-panel">
    <h2 class="settings-panel__title">
      Settings
    </h2>

    <div class="settings-group">
      <label class="settings-label" for="server-url">Server URL</label>
      <input
        id="server-url"
        v-model="settings.serverUrl"
        type="text"
        placeholder="ws://localhost:5000/ws"
        class="settings-input"
      >
    </div>

    <div class="settings-group">
      <label class="settings-label" for="token">Auth Token</label>
      <input
        id="token"
        v-model="settings.token"
        type="password"
        placeholder="JWT token (optional)"
        class="settings-input"
      >
    </div>

    <div class="settings-group">
      <label class="settings-label" for="language">Language</label>
      <select id="language" v-model="settings.language" class="settings-input">
        <option value="fr">
          Francais
        </option>
        <option value="en">
          English
        </option>
      </select>
    </div>

    <div class="settings-group">
      <label class="settings-label" for="avatar-mode">Avatar Mode</label>
      <select id="avatar-mode" v-model="settings.avatarMode" class="settings-input">
        <option value="vrm">
          3D (VRM)
        </option>
        <option value="live2d">
          2D (Live2D)
        </option>
      </select>
    </div>

    <div class="settings-group">
      <label class="settings-label" for="vad-threshold">
        Voice Threshold: {{ settings.vadThreshold.toFixed(2) }}
      </label>
      <input
        id="vad-threshold"
        v-model.number="settings.vadThreshold"
        type="range"
        min="0.1"
        max="0.95"
        step="0.05"
        class="settings-range"
      >
    </div>

    <div class="settings-actions">
      <button class="settings-btn settings-btn--reset" @click="settings.reset()">
        Reset defaults
      </button>
    </div>
  </div>
</template>

<style scoped>
.settings-panel {
  padding: 1rem;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  background: #fff;
  max-width: 480px;
}

.settings-panel__title {
  font-size: 1.125rem;
  font-weight: 600;
  color: #1e293b;
  margin: 0 0 1rem;
}

.settings-group {
  margin-bottom: 0.75rem;
}

.settings-label {
  display: block;
  font-size: 0.8rem;
  font-weight: 500;
  color: #475569;
  margin-bottom: 0.25rem;
}

.settings-input {
  width: 100%;
  padding: 0.5rem;
  border: 1px solid #cbd5e1;
  border-radius: 4px;
  font-size: 0.875rem;
  font-family: inherit;
  background: #fff;
  box-sizing: border-box;
}

.settings-input:focus {
  outline: none;
  border-color: #3b82f6;
}

.settings-range {
  width: 100%;
  cursor: pointer;
}

.settings-actions {
  margin-top: 1rem;
  display: flex;
  gap: 0.5rem;
}

.settings-btn {
  padding: 0.5rem 1rem;
  border: none;
  border-radius: 4px;
  cursor: pointer;
  font-size: 0.8rem;
  font-weight: 500;
}

.settings-btn--reset {
  background: #e2e8f0;
  color: #475569;
}

.settings-btn--reset:hover {
  background: #cbd5e1;
}
</style>
