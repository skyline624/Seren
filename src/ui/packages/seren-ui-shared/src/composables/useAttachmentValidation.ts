import {
  ATTACHMENT_MAX_COUNT,
  ATTACHMENT_MAX_PER_BYTES,
  ATTACHMENT_MAX_TOTAL_BYTES,
  isAcceptedMimeType,
} from './useAttachmentConstraints'

/**
 * Mirror of the server's `AttachmentValidationError` constants.
 * Keep aligned with
 * `src/server/Seren.Application/Chat/Attachments/AttachmentValidationError.cs`.
 */
export type AttachmentValidationCode =
  | 'unsupported_mime'
  | 'too_large'
  | 'total_too_large'
  | 'too_many'
  | 'invalid_base64'
  | 'magic_mismatch'
  | 'size_mismatch'
  | 'invalid_filename'

export type AttachmentValidationResult<T> =
  | { ok: true, value: T }
  | { ok: false, code: AttachmentValidationCode, message: string, fileName?: string }

/**
 * A single attachment the user has picked locally but hasn't sent yet.
 * Carries the original File so we can render a blob URL thumbnail and
 * only base64-encode at send time (sparing memory on gallery-picked
 * payloads the user may remove before sending).
 */
export interface PendingAttachment {
  /** Client-only id, stable for the lifetime of the composer. */
  id: string
  file: File
  mimeType: string
  fileName: string
  byteSize: number
  /** Optional blob URL for image thumbnails; null for docs. Call
   * `URL.revokeObjectURL(previewUrl)` when the attachment is removed. */
  previewUrl: string | null
}

/**
 * Validate a single File against the server constraints. Returns either a
 * ready-to-display `PendingAttachment` or a typed error the UI can map to
 * an i18n key (`chat.attachments.errors.<code>`).
 */
export function validateAttachment(file: File): AttachmentValidationResult<PendingAttachment> {
  if (!file.name || file.name.trim().length === 0) {
    return { ok: false, code: 'invalid_filename', message: 'File name is required.' }
  }

  const mime = (file.type ?? '').toLowerCase()
  if (!isAcceptedMimeType(mime)) {
    return {
      ok: false,
      code: 'unsupported_mime',
      message: `Unsupported file type '${file.type || 'unknown'}'.`,
      fileName: file.name,
    }
  }

  if (file.size <= 0 || file.size > ATTACHMENT_MAX_PER_BYTES) {
    return {
      ok: false,
      code: 'too_large',
      message: `File exceeds the ${ATTACHMENT_MAX_PER_BYTES / (1024 * 1024)} MiB cap.`,
      fileName: file.name,
    }
  }

  const previewUrl = mime.startsWith('image/') && !mime.includes('heic') && !mime.includes('heif')
    ? URL.createObjectURL(file)
    : null

  return {
    ok: true,
    value: {
      id: `att_${Date.now()}_${Math.random().toString(36).slice(2, 9)}`,
      file,
      mimeType: mime,
      fileName: file.name,
      byteSize: file.size,
      previewUrl,
    },
  }
}

/**
 * Validate a batch against global (count + aggregate bytes) caps. Returns
 * on first error so the UI can surface one actionable message.
 */
export function validateAttachmentBatch(
  current: PendingAttachment[],
  incoming: File[],
): AttachmentValidationResult<PendingAttachment[]> {
  if (incoming.length === 0) {
    return { ok: true, value: [] }
  }

  if (current.length + incoming.length > ATTACHMENT_MAX_COUNT) {
    return {
      ok: false,
      code: 'too_many',
      message: `At most ${ATTACHMENT_MAX_COUNT} attachments per message.`,
    }
  }

  let runningTotal = current.reduce((s, a) => s + a.byteSize, 0)
  const accepted: PendingAttachment[] = []

  for (const file of incoming) {
    const single = validateAttachment(file)
    if (!single.ok) {
      // Revoke any URLs we created along the way.
      for (const a of accepted) {
        if (a.previewUrl) URL.revokeObjectURL(a.previewUrl)
      }
      return single
    }
    runningTotal += single.value.byteSize
    if (runningTotal > ATTACHMENT_MAX_TOTAL_BYTES) {
      for (const a of accepted) {
        if (a.previewUrl) URL.revokeObjectURL(a.previewUrl)
      }
      if (single.value.previewUrl) URL.revokeObjectURL(single.value.previewUrl)
      return {
        ok: false,
        code: 'total_too_large',
        message: `Aggregate size exceeds the ${ATTACHMENT_MAX_TOTAL_BYTES / (1024 * 1024)} MiB cap.`,
      }
    }
    accepted.push(single.value)
  }

  return { ok: true, value: accepted }
}

/**
 * Read a File's bytes as base64 (no `data:` URI prefix), matching the
 * format the hub expects on `TextInputPayload.attachments[].content`.
 */
export async function readAsBase64(file: File): Promise<string> {
  const buffer = await file.arrayBuffer()
  // btoa over a binary string works up to ~8 MiB reliably; our cap is 5 MiB
  // so this path stays safely inside the browser's string limit.
  const bytes = new Uint8Array(buffer)
  let binary = ''
  const chunkSize = 0x8000
  for (let i = 0; i < bytes.length; i += chunkSize) {
    binary += String.fromCharCode(...bytes.subarray(i, i + chunkSize))
  }
  return btoa(binary)
}

/** Human-readable size label (e.g. "1.2 MB"). */
export function formatByteSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}
