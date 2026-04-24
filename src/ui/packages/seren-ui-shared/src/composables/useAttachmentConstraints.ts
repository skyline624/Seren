/**
 * Mirror of the server's `AttachmentConstraints` (C# class at
 * `src/server/Seren.Application/Chat/Attachments/AttachmentConstraints.cs`).
 * Kept in lockstep via the `GET /api/chat/attachments/constraints` contract
 * test — drift fails the vitest suite before it can ever reach production.
 *
 * ⚠ When bumping a limit here, bump the server file in the same PR.
 */

/** Max raw (pre-base64) bytes of a single attachment. */
export const ATTACHMENT_MAX_PER_BYTES = 5 * 1024 * 1024

/** Max aggregate raw bytes of all attachments on one message. */
export const ATTACHMENT_MAX_TOTAL_BYTES = 20 * 1024 * 1024

/** Hard cap on attachment count per message. */
export const ATTACHMENT_MAX_COUNT = 8

/** Images forwarded verbatim to OpenClaw as multimodal attachments. */
export const ATTACHMENT_IMAGE_MIME_TYPES = [
  'image/jpeg',
  'image/png',
  'image/webp',
  'image/gif',
  'image/heic',
  'image/heif',
] as const

/** Documents whose text is extracted server-side and folded into the user message. */
export const ATTACHMENT_DOCUMENT_MIME_TYPES = [
  'application/pdf',
  'text/plain',
  'text/markdown',
  'text/csv',
] as const

/** Union of all accepted MIME types — suitable for a file picker `accept=` string. */
export const ATTACHMENT_ALL_MIME_TYPES: readonly string[] = [
  ...ATTACHMENT_IMAGE_MIME_TYPES,
  ...ATTACHMENT_DOCUMENT_MIME_TYPES,
]

/** Convenience: comma-joined MIME list usable as `<input accept="…">`. */
export const ATTACHMENT_ACCEPT_ATTRIBUTE = ATTACHMENT_ALL_MIME_TYPES.join(',')

export function isImageMimeType(mime: string): boolean {
  return (ATTACHMENT_IMAGE_MIME_TYPES as readonly string[]).includes(mime.toLowerCase())
}

export function isDocumentMimeType(mime: string): boolean {
  return (ATTACHMENT_DOCUMENT_MIME_TYPES as readonly string[]).includes(mime.toLowerCase())
}

export function isAcceptedMimeType(mime: string): boolean {
  return isImageMimeType(mime) || isDocumentMimeType(mime)
}

/** Shape returned by `GET /api/chat/attachments/constraints`. */
export interface AttachmentConstraintsResponse {
  maxPerAttachmentBytes: number
  maxTotalBytes: number
  maxCount: number
  imageMimeTypes: string[]
  documentMimeTypes: string[]
}
