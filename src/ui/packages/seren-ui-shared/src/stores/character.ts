import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

export interface CharacterDto {
  id: string
  name: string
  systemPrompt: string
  vrmAssetPath?: string | null
  voice?: string | null
  agentId?: string | null
  isActive: boolean
  createdAt: string
  updatedAt: string
}

export interface CreateCharacterInput {
  name: string
  systemPrompt: string
  vrmAssetPath?: string | null
  voice?: string | null
  agentId?: string | null
}

export const useCharacterStore = defineStore('character', () => {
  const characters = ref<CharacterDto[]>([])
  const isLoading = ref(false)
  const error = ref<string | null>(null)
  const baseUrl = ref('')

  const activeCharacter = computed(() =>
    characters.value.find(c => c.isActive) ?? null,
  )

  function setBaseUrl(url: string): void {
    // Derive REST base URL from the WebSocket URL
    // ws://localhost:5000/ws → http://localhost:5000
    const normalized = url
      .replace(/\/ws\/?$/, '')
      .replace(/^ws:/, 'http:')
      .replace(/^wss:/, 'https:')
    baseUrl.value = normalized
  }

  async function fetchAll(): Promise<void> {
    if (!baseUrl.value) return
    isLoading.value = true
    error.value = null
    try {
      const res = await fetch(`${baseUrl.value}/api/characters`)
      if (!res.ok) {
        throw new Error(`Failed to fetch characters: ${res.status}`)
      }
      characters.value = await res.json()
    }
    catch (e) {
      error.value = e instanceof Error ? e.message : 'Failed to load characters'
    }
    finally {
      isLoading.value = false
    }
  }

  async function create(input: CreateCharacterInput): Promise<CharacterDto | null> {
    if (!baseUrl.value) return null
    error.value = null
    try {
      const res = await fetch(`${baseUrl.value}/api/characters`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(input),
      })
      if (!res.ok) {
        throw new Error(`Failed to create character: ${res.status}`)
      }
      const created: CharacterDto = await res.json()
      characters.value.push(created)
      return created
    }
    catch (e) {
      error.value = e instanceof Error ? e.message : 'Failed to create character'
      return null
    }
  }

  async function activate(id: string): Promise<void> {
    if (!baseUrl.value) return
    error.value = null
    try {
      const res = await fetch(`${baseUrl.value}/api/characters/${id}/activate`, {
        method: 'POST',
      })
      if (!res.ok) {
        throw new Error(`Failed to activate character: ${res.status}`)
      }
      // Update local state
      for (const c of characters.value) {
        c.isActive = c.id === id
      }
    }
    catch (e) {
      error.value = e instanceof Error ? e.message : 'Failed to activate character'
    }
  }

  async function remove(id: string): Promise<void> {
    if (!baseUrl.value) return
    error.value = null
    try {
      const res = await fetch(`${baseUrl.value}/api/characters/${id}`, {
        method: 'DELETE',
      })
      if (!res.ok) {
        throw new Error(`Failed to delete character: ${res.status}`)
      }
      characters.value = characters.value.filter(c => c.id !== id)
    }
    catch (e) {
      error.value = e instanceof Error ? e.message : 'Failed to delete character'
    }
  }

  return {
    characters,
    activeCharacter,
    isLoading,
    error,
    setBaseUrl,
    fetchAll,
    create,
    activate,
    remove,
  }
})
