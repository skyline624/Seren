/**
 * Wire-format snapshot of a download lifecycle. Status strings mirror the
 * server-side {@link ModelDownloadStatus} enum (lower-cased for JS).
 */
export interface ModelDownloadStateDto {
  status: 'idle' | 'downloading' | 'completed' | 'failed'
  bytesDone: number
  bytesTotal: number
  error: string | null
}

/**
 * Wire-format snapshot of one STT bundle. The UI consumes this from
 * {@link listModels}; `download` is null when no transfer has been
 * requested for this variant.
 */
export interface VoxMindModel {
  id: string
  engineFamily: 'parakeet' | 'whisper'
  displayKey: string
  approxSizeMb: number
  isDownloaded: boolean
  isSystemManaged: boolean
  download: ModelDownloadStateDto | null
}

function endpoint(serverUrl: string, path: string): string {
  const base = serverUrl.replace(/\/$/, '')
  return `${base}${path}`
}

/**
 * Returns the full catalog with on-disk presence flags. Throws when the
 * request fails — callers should surface the message to the UI rather
 * than silently rendering an empty list.
 */
export async function listModels(serverUrl: string): Promise<VoxMindModel[]> {
  const r = await fetch(endpoint(serverUrl, '/api/voxmind/models'), {
    method: 'GET',
    credentials: 'include',
  })
  if (!r.ok) {
    throw new Error(`listModels failed: ${r.status} ${r.statusText}`)
  }
  // Server returns PascalCase (System.Text.Json default), normalise to camelCase.
  const raw = await r.json()
  return raw.map(normaliseModel)
}

/**
 * Fires the download for one variant. Returns immediately with the
 * just-accepted state — callers poll {@link getDownloadStatus} to track
 * progress.
 */
export async function startDownload(serverUrl: string, id: string): Promise<void> {
  const r = await fetch(endpoint(serverUrl, `/api/voxmind/models/${encodeURIComponent(id)}/download`), {
    method: 'POST',
    credentials: 'include',
  })
  if (!r.ok && r.status !== 202) {
    throw new Error(`startDownload(${id}) failed: ${r.status} ${r.statusText}`)
  }
}

/** Polls the current download lifecycle for a variant. */
export async function getDownloadStatus(serverUrl: string, id: string): Promise<ModelDownloadStateDto> {
  const r = await fetch(endpoint(serverUrl, `/api/voxmind/models/${encodeURIComponent(id)}/download/status`), {
    method: 'GET',
    credentials: 'include',
  })
  if (!r.ok) {
    throw new Error(`getDownloadStatus(${id}) failed: ${r.status} ${r.statusText}`)
  }
  return normaliseDownloadState(await r.json())
}

/**
 * Deletes the on-disk bundle for a variant. The server rejects with 409
 * when the deletion would leave zero engines available — surface that
 * conflict to the user via the returned `Error`.
 */
export async function deleteModel(serverUrl: string, id: string): Promise<void> {
  const r = await fetch(endpoint(serverUrl, `/api/voxmind/models/${encodeURIComponent(id)}`), {
    method: 'DELETE',
    credentials: 'include',
  })
  if (r.status === 204) return
  if (r.status === 409) {
    throw new Error(await safeReadError(r) ?? 'deleteLastBlocked')
  }
  if (!r.ok) {
    throw new Error(`deleteModel(${id}) failed: ${r.status} ${r.statusText}`)
  }
}

function normaliseModel(raw: Record<string, unknown>): VoxMindModel {
  return {
    id: String(raw.id ?? raw.Id),
    engineFamily: String(raw.engineFamily ?? raw.EngineFamily) as VoxMindModel['engineFamily'],
    displayKey: String(raw.displayKey ?? raw.DisplayKey),
    approxSizeMb: Number(raw.approxSizeMb ?? raw.ApproxSizeMb ?? 0),
    isDownloaded: Boolean(raw.isDownloaded ?? raw.IsDownloaded),
    isSystemManaged: Boolean(raw.isSystemManaged ?? raw.IsSystemManaged),
    download: raw.download != null
      ? normaliseDownloadState(raw.download as Record<string, unknown>)
      : raw.Download != null
        ? normaliseDownloadState(raw.Download as Record<string, unknown>)
        : null,
  }
}

function normaliseDownloadState(raw: Record<string, unknown>): ModelDownloadStateDto {
  return {
    status: String(raw.status ?? raw.Status ?? 'idle').toLowerCase() as ModelDownloadStateDto['status'],
    bytesDone: Number(raw.bytesDone ?? raw.BytesDone ?? 0),
    bytesTotal: Number(raw.bytesTotal ?? raw.BytesTotal ?? 0),
    error: (raw.error ?? raw.Error ?? null) as string | null,
  }
}

async function safeReadError(r: Response): Promise<string | null> {
  try {
    const body = await r.json()
    if (body && typeof body === 'object' && 'error' in body) {
      return String((body as { error: unknown }).error)
    }
  }
  catch {
    /* ignore */
  }
  return null
}
