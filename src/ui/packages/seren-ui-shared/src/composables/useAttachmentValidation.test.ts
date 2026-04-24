import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  ATTACHMENT_MAX_COUNT,
  ATTACHMENT_MAX_PER_BYTES,
  ATTACHMENT_MAX_TOTAL_BYTES,
} from './useAttachmentConstraints'
import {
  formatByteSize,
  validateAttachment,
  validateAttachmentBatch,
  type PendingAttachment,
} from './useAttachmentValidation'

// jsdom ships createObjectURL but not revokeObjectURL in every version.
const createObjectUrlSpy = vi
  .spyOn(URL, 'createObjectURL')
  .mockImplementation(() => 'blob:fake')
const revokeObjectUrlSpy = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {})

afterEach(() => {
  createObjectUrlSpy.mockClear()
  revokeObjectUrlSpy.mockClear()
})

function makeFile(name: string, mimeType: string, size = 128): File {
  const blob = new Blob([new Uint8Array(size)], { type: mimeType })
  return new File([blob], name, { type: mimeType })
}

describe('validateAttachment', () => {
  it('accepts an in-whitelist image and returns a blob URL', () => {
    const result = validateAttachment(makeFile('pic.jpg', 'image/jpeg'))

    expect(result.ok).toBe(true)
    if (result.ok) {
      expect(result.value.mimeType).toBe('image/jpeg')
      expect(result.value.fileName).toBe('pic.jpg')
      expect(result.value.previewUrl).toBe('blob:fake')
    }
  })

  it('does not create a blob URL for non-image MIME', () => {
    const result = validateAttachment(makeFile('doc.pdf', 'application/pdf'))

    expect(result.ok).toBe(true)
    if (result.ok) {
      expect(result.value.previewUrl).toBeNull()
    }
  })

  it('rejects unsupported MIME types', () => {
    const result = validateAttachment(makeFile('malware.exe', 'application/x-msdownload'))

    expect(result.ok).toBe(false)
    if (!result.ok) expect(result.code).toBe('unsupported_mime')
  })

  it('rejects oversized files', () => {
    const huge = new File([new Uint8Array(ATTACHMENT_MAX_PER_BYTES + 1)], 'big.jpg', {
      type: 'image/jpeg',
    })
    const result = validateAttachment(huge)

    expect(result.ok).toBe(false)
    if (!result.ok) expect(result.code).toBe('too_large')
  })

  it('rejects a zero-byte file', () => {
    const empty = new File([], 'empty.jpg', { type: 'image/jpeg' })
    const result = validateAttachment(empty)

    expect(result.ok).toBe(false)
    if (!result.ok) expect(result.code).toBe('too_large')
  })

  it('rejects a blank file name', () => {
    const nameless = new File([new Uint8Array(8)], '   ', { type: 'image/jpeg' })
    const result = validateAttachment(nameless)

    expect(result.ok).toBe(false)
    if (!result.ok) expect(result.code).toBe('invalid_filename')
  })

  it('skips the blob URL for HEIC (browsers cannot thumbnail those)', () => {
    const result = validateAttachment(makeFile('shot.heic', 'image/heic'))

    expect(result.ok).toBe(true)
    if (result.ok) expect(result.value.previewUrl).toBeNull()
  })
})

describe('validateAttachmentBatch', () => {
  it('returns empty on empty input', () => {
    const result = validateAttachmentBatch([], [])
    expect(result.ok).toBe(true)
    if (result.ok) expect(result.value).toEqual([])
  })

  it('rejects when the incoming batch exceeds the MaxCount cap', () => {
    const files = Array.from({ length: ATTACHMENT_MAX_COUNT + 1 }).map((_, i) =>
      makeFile(`f${i}.jpg`, 'image/jpeg'),
    )
    const result = validateAttachmentBatch([], files)

    expect(result.ok).toBe(false)
    if (!result.ok) expect(result.code).toBe('too_many')
  })

  it('rejects when the running total crosses MaxTotalBytes', () => {
    // 12 MiB already accepted + three 5 MiB newcomers = 27 MiB, > 20 MiB cap.
    // First two pass the per-attachment check; the third pushes the
    // running total past MaxTotalBytes and triggers the global reject.
    const baseline: PendingAttachment[] = [{
      id: 'seed',
      file: makeFile('seed.jpg', 'image/jpeg'),
      mimeType: 'image/jpeg',
      fileName: 'seed.jpg',
      byteSize: 12 * 1024 * 1024,
      previewUrl: null,
    }]
    const incoming = [
      makeFile('a.jpg', 'image/jpeg', ATTACHMENT_MAX_PER_BYTES),
      makeFile('b.jpg', 'image/jpeg', ATTACHMENT_MAX_PER_BYTES),
      makeFile('c.jpg', 'image/jpeg', ATTACHMENT_MAX_PER_BYTES),
    ]
    const result = validateAttachmentBatch(baseline, incoming)

    expect(result.ok).toBe(false)
    if (!result.ok) expect(result.code).toBe('total_too_large')
    // Caller should also see any created blob URLs revoked.
    expect(revokeObjectUrlSpy).toHaveBeenCalled()
  })

  it('accepts a valid mixed batch and returns the classified list', () => {
    const incoming = [
      makeFile('pic.jpg', 'image/jpeg'),
      makeFile('doc.pdf', 'application/pdf'),
    ]
    const result = validateAttachmentBatch([], incoming)

    expect(result.ok).toBe(true)
    if (result.ok) expect(result.value.length).toBe(2)
  })
})

describe('formatByteSize', () => {
  it('formats bytes', () => expect(formatByteSize(512)).toBe('512 B'))
  it('formats KB', () => expect(formatByteSize(2048)).toBe('2.0 KB'))
  it('formats MB', () => expect(formatByteSize(5 * 1024 * 1024)).toBe('5.0 MB'))
})

describe('ATTACHMENT_MAX_TOTAL_BYTES constant', () => {
  it('is strictly larger than the per-attachment cap', () => {
    expect(ATTACHMENT_MAX_TOTAL_BYTES).toBeGreaterThan(ATTACHMENT_MAX_PER_BYTES)
  })
})
