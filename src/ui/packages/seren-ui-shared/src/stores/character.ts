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
  /** Optional first-message greeting (set only by Character Card v3 import). */
  greeting?: string | null
  /** Optional raw description harvested from a CCv3 card. */
  description?: string | null
  /** Path (relative to the avatar store) of the imported 2D avatar PNG,
   * when present. Served by `GET /api/characters/{id}/avatar`. */
  avatarImagePath?: string | null
  /** Tags harvested from a CCv3 card. Empty for manually-created characters. */
  tags?: string[]
  /** Opaque JSON blob preserving CCv3 fields Seren does not yet interpret
   * (character_book, alternate_greetings, …). Surfaced only to the client
   * for future features; not rendered directly in the UI. */
  importMetadataJson?: string | null
}

/** Machine-readable codes returned by `POST /api/characters/import` on
 * failure. Mirrored from `Seren.Contracts.Characters.CharacterImportError`. */
export type CharacterImportErrorCode =
  | 'invalid_card'
  | 'unsupported_spec'
  | 'card_too_large'
  | 'empty_prompt'
  | 'malformed_png'
  | 'malformed_json'

export interface CharacterImportErrorResponse {
  code: CharacterImportErrorCode | string
  message: string
  details?: string | null
}

export interface ImportCharacterResult {
  character: CharacterDto
  warnings: string[]
}

/** Machine-readable codes returned by `POST /api/characters/capture` on
 * failure. Mirrored from `Seren.Contracts.Characters.PersonaCaptureError`. */
export type PersonaCaptureErrorCode =
  | 'workspace_empty'
  | 'invalid_persona'
  | 'no_workspace_configured'

export interface PersonaCaptureErrorResponse {
  code: PersonaCaptureErrorCode | string
  message: string
  details?: string | null
}

export interface CapturedPersonaResult {
  character: CharacterDto
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
  /**
   * Structured state of the most recent import attempt. `null` until
   * the user triggers an import. The UI reads `status` to render the
   * toast/banner and `errorCode` to pick the localised message.
   */
  const lastImport = ref<
    | { status: 'ok', characterName: string, warnings: string[] }
    | { status: 'error', errorCode: string, errorMessage: string }
    | null
  >(null)
  /**
   * Structured state of the most recent capture attempt — mirror of
   * `lastImport`. Kept separate so the UI can render distinct toasts
   * (capture vs CCv3 import) without coupling the two flows.
   */
  const lastCapture = ref<
    | { status: 'ok', characterName: string }
    | { status: 'error', errorCode: string, errorMessage: string }
    | null
  >(null)

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

  async function update(id: string, input: CreateCharacterInput): Promise<CharacterDto | null> {
    if (!baseUrl.value) return null
    error.value = null
    try {
      const res = await fetch(`${baseUrl.value}/api/characters/${id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(input),
      })
      if (!res.ok) {
        throw new Error(`Failed to update character: ${res.status}`)
      }
      const updated: CharacterDto = await res.json()
      const idx = characters.value.findIndex(c => c.id === id)
      if (idx >= 0) characters.value[idx] = updated
      return updated
    }
    catch (e) {
      error.value = e instanceof Error ? e.message : 'Failed to update character'
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

  /**
   * Upload a Character Card v3 file (.png / .apng / .json) to the hub,
   * parse it server-side, and add the resulting persona to the local
   * character list. Surfaces typed errors via `lastImport` so the UI
   * can pick a localised message via the `characters.import.errors.<code>`
   * i18n key instead of showing the raw server string.
   */
  async function importCard(file: File, activate = false): Promise<ImportCharacterResult | null> {
    if (!baseUrl.value) return null
    lastImport.value = null

    const form = new FormData()
    form.append('file', file, file.name)
    if (activate) form.append('activateOnImport', 'true')

    try {
      const res = await fetch(`${baseUrl.value}/api/characters/import`, {
        method: 'POST',
        body: form,
      })

      if (!res.ok) {
        // Server returns a typed { code, message, details? } on 4xx.
        let errorBody: CharacterImportErrorResponse | null = null
        try { errorBody = await res.json() }
        catch { /* fall through — non-JSON error */ }
        const code = errorBody?.code ?? 'invalid_card'
        const message = errorBody?.message ?? `Import failed: ${res.status}`
        lastImport.value = { status: 'error', errorCode: code, errorMessage: message }
        return null
      }

      const payload = await res.json() as ImportCharacterResult
      characters.value.push(payload.character)
      if (activate) {
        for (const c of characters.value) c.isActive = c.id === payload.character.id
      }
      lastImport.value = { status: 'ok', characterName: payload.character.name, warnings: payload.warnings }
      return payload
    }
    catch (e) {
      const message = e instanceof Error ? e.message : 'Unexpected import failure'
      lastImport.value = { status: 'error', errorCode: 'invalid_card', errorMessage: message }
      return null
    }
  }

  /** URL to the imported 2D avatar for a given character id, or `null`
   * when the character has no avatar. The URL is stable for the life of
   * the record — delete + re-import to replace it. */
  function avatarUrl(character: CharacterDto | null | undefined): string | null {
    if (!character || !baseUrl.value) return null
    if (!character.avatarImagePath) return null
    return `${baseUrl.value}/api/characters/${character.id}/avatar`
  }

  /**
   * Capture OpenClaw's current workspace persona (IDENTITY.md +
   * SOUL.md) as a brand-new Seren character. Inverse of the persona-
   * writer flow — useful after OpenClaw's free-form onboarding. Maps
   * typed server errors to i18n keys under `characters.capture.errors`.
   */
  async function capturePersona(activate = false): Promise<CapturedPersonaResult | null> {
    if (!baseUrl.value) return null
    lastCapture.value = null

    const url = `${baseUrl.value}/api/characters/capture${activate ? '?activate=true' : ''}`
    try {
      const res = await fetch(url, { method: 'POST' })

      if (!res.ok) {
        let errorBody: PersonaCaptureErrorResponse | null = null
        try { errorBody = await res.json() }
        catch { /* non-JSON error body */ }
        const code = errorBody?.code ?? (res.status === 404 ? 'workspace_empty' : 'invalid_persona')
        const message = errorBody?.message ?? `Capture failed: ${res.status}`
        lastCapture.value = { status: 'error', errorCode: code, errorMessage: message }
        return null
      }

      const payload = await res.json() as CapturedPersonaResult
      characters.value.push(payload.character)
      if (activate) {
        for (const c of characters.value) c.isActive = c.id === payload.character.id
      }
      lastCapture.value = { status: 'ok', characterName: payload.character.name }
      return payload
    }
    catch (e) {
      const message = e instanceof Error ? e.message : 'Unexpected capture failure'
      lastCapture.value = { status: 'error', errorCode: 'invalid_persona', errorMessage: message }
      return null
    }
  }

  /** Build the URL the browser hits to download a character as JSON.
   * The server stamps `Content-Disposition: attachment; filename=...`
   * so anchoring an <a href download> to this URL opens a save dialog
   * with the right filename. */
  function downloadUrl(character: CharacterDto | null | undefined): string | null {
    if (!character || !baseUrl.value) return null
    return `${baseUrl.value}/api/characters/${character.id}/download`
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
    lastImport,
    lastCapture,
    setBaseUrl,
    fetchAll,
    create,
    update,
    activate,
    remove,
    importCard,
    avatarUrl,
    capturePersona,
    downloadUrl,
  }
})
