import { ATTACHMENT_ACCEPT_ATTRIBUTE } from './useAttachmentConstraints'

/**
 * Unified attachment picker abstraction. The web / desktop surfaces use a
 * hidden `<input type="file">` + drag-drop + clipboard paste. Mobile
 * (Capacitor) adds a separate entry point for camera / gallery and is
 * wired in a follow-up chantier; the runtime platform check here keeps a
 * single call-site for the ChatPanel.
 */
export interface AttachmentPickerOptions {
  /** Called when the user drops / picks / pastes one or more files. */
  onFiles: (files: File[]) => void
}

/**
 * Open a native file picker limited to the allowed MIME types. Returns
 * when the user either picks files or dismisses the dialog.
 */
export function pickFilesFromDialog(onFiles: (files: File[]) => void): void {
  const input = document.createElement('input')
  input.type = 'file'
  input.multiple = true
  input.accept = ATTACHMENT_ACCEPT_ATTRIBUTE
  input.style.display = 'none'
  input.addEventListener('change', () => {
    const files = input.files ? [...input.files] : []
    document.body.removeChild(input)
    if (files.length > 0) onFiles(files)
  })
  // Some browsers fire nothing when the user cancels — no cleanup hook
  // needed since the input is collected by GC once it leaves the DOM.
  document.body.appendChild(input)
  input.click()
}

/** Extract File objects from a drag-drop DataTransfer, filtering entries
 * that aren't files (URLs, HTML). Safe on empty transfers. */
export function extractDropFiles(dataTransfer: DataTransfer | null): File[] {
  if (!dataTransfer) return []
  const items = dataTransfer.files
  if (!items || items.length === 0) return []
  return [...items]
}

/** Extract File objects from a paste event. Browsers expose pasted
 * images as `items[]` with kind:'file'; regular copy-paste of text
 * yields an empty file array here, which is the correct no-op. */
export function extractPasteFiles(event: ClipboardEvent): File[] {
  const items = event.clipboardData?.items
  if (!items) return []
  const files: File[] = []
  for (const item of items) {
    if (item.kind === 'file') {
      const file = item.getAsFile()
      if (file) files.push(file)
    }
  }
  return files
}
