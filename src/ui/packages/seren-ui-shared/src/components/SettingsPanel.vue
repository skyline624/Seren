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
  padding: 0;
  border: none;
  background: transparent;
  max-width: 100%;
}

.settings-panel__title {
  display: none;
}

.settings-group {
  margin-bottom: 1rem;
}

.settings-label {
  display: block;
  font-size: 0.8rem;
  font-weight: 500;
  color: #94a3b8;
  margin-bottom: 0.375rem;
}

.settings-input {
  width: 100%;
  padding: 0.5rem 0.75rem;
  border: 1px solid rgba(100, 180, 200, 0.2);
  border-radius: 8px;
  font-size: 0.875rem;
  font-family: inherit;
  background: rgba(15, 23, 42, 0.6);
  color: #e2e8f0;
  box-sizing: border-box;
  transition: border-color 0.2s;
}

.settings-input:focus {
  outline: none;
  border-color: rgba(13, 148, 136, 0.6);
}

.settings-input option {
  background: #1e293b;
  color: #e2e8f0;
}

.settings-range {
  width: 100%;
  cursor: pointer;
  accent-color: #0d9488;
}

.settings-actions {
  margin-top: 1.5rem;
  display: flex;
  gap: 0.5rem;
}

.settings-btn {
  padding: 0.5rem 1rem;
  border: none;
  border-radius: 8px;
  cursor: pointer;
  font-size: 0.8rem;
  font-weight: 500;
}

.settings-btn--reset {
  background: rgba(100, 180, 200, 0.15);
  color: #94a3b8;
  border: 1px solid rgba(100, 180, 200, 0.2);
}

.settings-btn--reset:hover {
  background: rgba(100, 180, 200, 0.25);
  color: #e2e8f0;
}
</style>
