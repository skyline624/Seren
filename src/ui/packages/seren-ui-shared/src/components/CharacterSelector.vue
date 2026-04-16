<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useCharacterStore } from '../stores/character'

const store = useCharacterStore()
const showCreateForm = ref(false)

// Create form fields
const newName = ref('')
const newPrompt = ref('')
const newVrmPath = ref('')
const newVoice = ref('')
const newAgentId = ref('')

onMounted(() => {
  store.fetchAll()
})

async function handleCreate(): Promise<void> {
  if (!newName.value.trim() || !newPrompt.value.trim()) return

  const created = await store.create({
    name: newName.value.trim(),
    systemPrompt: newPrompt.value.trim(),
    vrmAssetPath: newVrmPath.value.trim() || null,
    voice: newVoice.value.trim() || null,
    agentId: newAgentId.value.trim() || null,
  })

  if (created) {
    newName.value = ''
    newPrompt.value = ''
    newVrmPath.value = ''
    newVoice.value = ''
    newAgentId.value = ''
    showCreateForm.value = false
  }
}

function handleActivate(id: string): void {
  store.activate(id)
}

function handleDelete(id: string): void {
  store.remove(id)
}
</script>

<template>
  <div class="character-selector">
    <div class="character-selector__header">
      <span class="character-selector__title">Characters</span>
      <button class="character-selector__add" @click="showCreateForm = !showCreateForm">
        {{ showCreateForm ? 'Cancel' : '+ New' }}
      </button>
    </div>

    <div v-if="store.error" class="character-selector__error">
      {{ store.error }}
    </div>

    <!-- Create form -->
    <form v-if="showCreateForm" class="character-form" @submit.prevent="handleCreate">
      <input v-model="newName" placeholder="Name" required>
      <textarea v-model="newPrompt" placeholder="System prompt..." rows="2" required />
      <input v-model="newVrmPath" placeholder="VRM asset path (optional)">
      <input v-model="newVoice" placeholder="Voice (optional)">
      <input v-model="newAgentId" placeholder="Agent ID (optional)">
      <button type="submit" :disabled="!newName.trim() || !newPrompt.trim()">
        Create
      </button>
    </form>

    <!-- Character list -->
    <div v-if="store.isLoading" class="character-selector__loading">
      Loading...
    </div>
    <div v-else class="character-list">
      <div
        v-for="char in store.characters"
        :key="char.id"
        :class="['character-card', { 'character-card--active': char.isActive }]"
      >
        <div class="character-card__info">
          <span class="character-card__name">{{ char.name }}</span>
          <span v-if="char.isActive" class="character-card__badge">Active</span>
        </div>
        <div class="character-card__actions">
          <button
            v-if="!char.isActive"
            class="character-card__btn character-card__btn--activate"
            @click="handleActivate(char.id)"
          >
            Activate
          </button>
          <button
            class="character-card__btn character-card__btn--delete"
            @click="handleDelete(char.id)"
          >
            Delete
          </button>
        </div>
      </div>
      <div v-if="store.characters.length === 0 && !store.isLoading" class="character-selector__empty">
        No characters yet
      </div>
    </div>
  </div>
</template>

<style scoped>
.character-selector {
  padding: 0;
  border: none;
  background: transparent;
}

.character-selector__header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 0.75rem;
}

.character-selector__title {
  font-weight: 600;
  font-size: 0.875rem;
  color: #94a3b8;
}

.character-selector__add {
  font-size: 0.75rem;
  padding: 0.25rem 0.5rem;
  background: #0d9488;
  color: #fff;
  border: none;
  border-radius: 6px;
  cursor: pointer;
}

.character-selector__add:hover {
  background: #14b8a6;
}

.character-selector__error {
  color: #fca5a5;
  font-size: 0.75rem;
  margin-bottom: 0.5rem;
}

.character-selector__loading,
.character-selector__empty {
  color: #475569;
  font-size: 0.8rem;
  text-align: center;
  padding: 0.5rem;
}

.character-form {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  margin-bottom: 1rem;
  padding: 0.75rem;
  border: 1px solid rgba(100, 180, 200, 0.2);
  border-radius: 12px;
  background: rgba(15, 23, 42, 0.5);
}

.character-form input,
.character-form textarea {
  padding: 0.5rem 0.75rem;
  border: 1px solid rgba(100, 180, 200, 0.2);
  border-radius: 8px;
  font-size: 0.8rem;
  font-family: inherit;
  background: rgba(15, 23, 42, 0.6);
  color: #e2e8f0;
}

.character-form input::placeholder,
.character-form textarea::placeholder {
  color: #64748b;
}

.character-form textarea {
  resize: vertical;
}

.character-form button {
  padding: 0.5rem;
  background: #0d9488;
  color: #fff;
  border: none;
  border-radius: 8px;
  cursor: pointer;
  font-size: 0.8rem;
}

.character-form button:disabled {
  opacity: 0.35;
  cursor: not-allowed;
}

.character-list {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.character-card {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0.625rem 0.75rem;
  border: 1px solid rgba(100, 180, 200, 0.15);
  border-radius: 12px;
  background: rgba(30, 41, 59, 0.5);
}

.character-card--active {
  border-color: #0d9488;
  background: rgba(13, 148, 136, 0.1);
}

.character-card__info {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.character-card__name {
  font-size: 0.8rem;
  font-weight: 500;
  color: #e2e8f0;
}

.character-card__badge {
  font-size: 0.65rem;
  padding: 0.1rem 0.375rem;
  background: #0d9488;
  color: #fff;
  border-radius: 9999px;
}

.character-card__actions {
  display: flex;
  gap: 0.25rem;
}

.character-card__btn {
  font-size: 0.7rem;
  padding: 0.25rem 0.5rem;
  border: none;
  border-radius: 6px;
  cursor: pointer;
}

.character-card__btn--activate {
  background: rgba(100, 180, 200, 0.15);
  color: #94a3b8;
}

.character-card__btn--activate:hover {
  background: rgba(100, 180, 200, 0.25);
  color: #e2e8f0;
}

.character-card__btn--delete {
  background: rgba(239, 68, 68, 0.15);
  color: #fca5a5;
}

.character-card__btn--delete:hover {
  background: rgba(239, 68, 68, 0.25);
}
</style>
