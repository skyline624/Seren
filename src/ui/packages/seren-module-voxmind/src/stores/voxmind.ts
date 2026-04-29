import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { useConnectionSettingsStore, usePersistedRef } from '@seren/ui-shared'
import {
  deleteModel as apiDeleteModel,
  getDownloadStatus,
  listModels,
  startDownload,
  type ModelDownloadStateDto,
  type VoxMindModel,
} from '../api/modelsClient'

/**
 * Engine identifier persisted in localStorage and forwarded inline on every
 * voice request. Free-form string so new variants (whisper-tiny,
 * whisper-medium, …) automatically work without an SDK rev.
 */
export type VoxMindSttEngine = string

const DEFAULT_ENGINE: VoxMindSttEngine = 'parakeet'
const POLL_INTERVAL_MS = 1000

/**
 * Settings store for the VoxMind module: holds the active engine
 * (downloaded variants only), the catalog of models with on-disk presence
 * + active downloads, and the actions that drive download / delete /
 * select. Polls the server on user action — no automatic refresh, the UI
 * triggers a `refresh()` when the tab mounts.
 */
export const useVoxMindSettingsStore = defineStore('settings/voxmind', () => {
  const sttEngine = usePersistedRef<VoxMindSttEngine>(
    'seren/voxmind/sttEngine',
    DEFAULT_ENGINE,
  )

  const models = ref<VoxMindModel[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)
  const pollers = new Map<string, ReturnType<typeof setInterval>>()

  const downloadedModels = computed(() =>
    models.value.filter(m => m.isDownloaded || m.isSystemManaged),
  )

  function getServerUrl(): string {
    const conn = useConnectionSettingsStore()
    // Empty serverUrl falls back to same-origin — fetch resolves
    // /api/voxmind/* against the page host. Works in dev (Vite proxy) and
    // prod (web app served from the same origin as the API).
    return conn.serverUrl || ''
  }

  async function refresh(): Promise<void> {
    loading.value = true
    error.value = null
    try {
      const list = await listModels(getServerUrl())
      models.value = list
      migrateLegacyEngine()
      ensureActiveStillDownloaded()
    }
    catch (ex) {
      error.value = (ex as Error).message
    }
    finally {
      loading.value = false
    }
  }

  function migrateLegacyEngine(): void {
    if (sttEngine.value === 'whisper') {
      // Pre-multivariant value. Promote to the largest downloaded
      // whisper variant or fall back to parakeet so the dropdown-free
      // UI still has something to select.
      const downloadedWhispers = models.value
        .filter(m => m.engineFamily === 'whisper' && m.isDownloaded)
        .sort((a, b) => b.approxSizeMb - a.approxSizeMb)
      sttEngine.value = downloadedWhispers[0]?.id ?? 'parakeet'
    }
  }

  function ensureActiveStillDownloaded(): void {
    const active = models.value.find(m => m.id === sttEngine.value)
    if (!active || (!active.isDownloaded && !active.isSystemManaged)) {
      // Active engine no longer on disk (deleted from another tab,
      // failed download, etc.) — fall back to parakeet which is always
      // system-managed.
      sttEngine.value = 'parakeet'
    }
  }

  async function download(id: string): Promise<void> {
    error.value = null
    try {
      await startDownload(getServerUrl(), id)
      // Optimistic local state so the row immediately shows a spinner.
      patchModel(id, m => ({
        ...m,
        download: { status: 'downloading', bytesDone: 0, bytesTotal: 0, error: null },
      }))
      startPolling(id)
    }
    catch (ex) {
      error.value = (ex as Error).message
    }
  }

  function startPolling(id: string): void {
    stopPolling(id)
    const handle = setInterval(async () => {
      try {
        const state = await getDownloadStatus(getServerUrl(), id)
        patchModel(id, m => ({ ...m, download: state }))
        if (state.status === 'completed') {
          stopPolling(id)
          await refresh()
        }
        else if (state.status === 'failed') {
          stopPolling(id)
          error.value = state.error ?? 'downloadFailed'
        }
      }
      catch {
        stopPolling(id)
      }
    }, POLL_INTERVAL_MS)
    pollers.set(id, handle)
  }

  function stopPolling(id: string): void {
    const handle = pollers.get(id)
    if (handle != null) {
      clearInterval(handle)
      pollers.delete(id)
    }
  }

  async function remove(id: string): Promise<void> {
    error.value = null
    try {
      await apiDeleteModel(getServerUrl(), id)
      if (sttEngine.value === id) {
        // Server already validated that another engine is available;
        // fall back to parakeet (always system-managed).
        sttEngine.value = 'parakeet'
      }
      await refresh()
    }
    catch (ex) {
      error.value = (ex as Error).message
    }
  }

  function selectActive(id: string): void {
    const model = models.value.find(m => m.id === id)
    if (model && (model.isDownloaded || model.isSystemManaged)) {
      sttEngine.value = id
    }
  }

  function reset(): void {
    sttEngine.value = DEFAULT_ENGINE
    error.value = null
  }

  function patchModel(id: string, mutator: (m: VoxMindModel) => VoxMindModel): void {
    const idx = models.value.findIndex(m => m.id === id)
    const current = idx >= 0 ? models.value[idx] : undefined
    if (current) {
      models.value[idx] = mutator(current)
    }
  }

  return {
    sttEngine,
    models,
    loading,
    error,
    downloadedModels,
    refresh,
    download,
    remove,
    selectActive,
    reset,
  }
})

export type { VoxMindModel, ModelDownloadStateDto }
