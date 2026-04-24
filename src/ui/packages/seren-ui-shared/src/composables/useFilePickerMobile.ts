/**
 * Mobile-native file picker. Wraps <code>@capacitor/camera</code> (camera
 * capture + gallery picker) and <code>@capacitor/filesystem</code> (arbitrary
 * document picking) so the ChatPanel stays platform-agnostic. Delegates to
 * the web picker on non-native platforms (seren-web, seren-desktop webview)
 * because Capacitor isn't present there.
 *
 * ⚠ Native permissions declared by the host app:
 *   • iOS — `ios/App/App/Info.plist`:
 *     <key>NSCameraUsageDescription</key><string>Take photos for the chat</string>
 *     <key>NSPhotoLibraryUsageDescription</key><string>Attach photos from your library</string>
 *   • Android — `android/app/src/main/AndroidManifest.xml`:
 *     <uses-permission android:name="android.permission.CAMERA"/>
 *     <uses-permission android:name="android.permission.READ_MEDIA_IMAGES"/>
 *
 * When the user refuses the prompt, the plugin throws a typed error we
 * surface via <code>permission_denied</code> through the standard
 * validation error channel.
 */

import { pickFilesFromDialog } from './useAttachmentPicker'

/**
 * Deferred-import guard: Capacitor is only present in the mobile bundle.
 * The shared UI package does not depend on `@capacitor/*` — it would
 * pollute the web/desktop bundles. Silencing the type resolver here is
 * intentional; the runtime try/catch handles the missing-module case.
 */
// eslint-disable-next-line ts/no-explicit-any
async function loadCapacitor(): Promise<any | null> {
  try {
    // @ts-expect-error — optional peer, resolved at runtime by the mobile app only.
    return await import('@capacitor/core')
  }
  catch {
    return null
  }
}

// eslint-disable-next-line ts/no-explicit-any
async function loadCamera(): Promise<any | null> {
  try {
    // @ts-expect-error — optional peer, resolved at runtime by the mobile app only.
    return await import('@capacitor/camera')
  }
  catch {
    return null
  }
}

export async function isMobileNative(): Promise<boolean> {
  const cap = await loadCapacitor()
  return cap?.Capacitor?.isNativePlatform?.() ?? false
}

export interface MobilePickerResult {
  ok: true
  file: File
}

export interface MobilePickerError {
  ok: false
  code: 'permission_denied' | 'cancelled' | 'unknown'
  message: string
}

/**
 * Launch the native camera and return the captured photo as a web
 * <code>File</code> so callers can reuse the same validation path as the
 * web picker. Degrades to the file-picker dialog when the camera plugin
 * is unavailable (non-native or plugin not installed).
 */
export async function captureFromCamera(): Promise<MobilePickerResult | MobilePickerError | null> {
  const camera = await loadCamera()
  if (!camera) {
    pickFilesFromDialog(() => {})
    return null
  }

  try {
    const photo = await camera.Camera.getPhoto({
      quality: 90,
      allowEditing: false,
      source: camera.CameraSource.Camera,
      resultType: camera.CameraResultType.Uri,
    })
    return await photoToFile(photo)
  }
  catch (err) {
    return mapCameraError(err)
  }
}

/**
 * Let the user pick an image from the device gallery. Same return shape
 * as <code>captureFromCamera</code>. Uses <code>CameraSource.Photos</code>
 * which opens the native image picker (iOS Photos, Android gallery).
 */
export async function pickFromGallery(): Promise<MobilePickerResult | MobilePickerError | null> {
  const camera = await loadCamera()
  if (!camera) return null

  try {
    const photo = await camera.Camera.getPhoto({
      quality: 90,
      allowEditing: false,
      source: camera.CameraSource.Photos,
      resultType: camera.CameraResultType.Uri,
    })
    return await photoToFile(photo)
  }
  catch (err) {
    return mapCameraError(err)
  }
}

async function photoToFile(
  photo: { webPath?: string, path?: string, format?: string },
): Promise<MobilePickerResult | MobilePickerError> {
  const src = photo.webPath ?? photo.path
  if (!src) {
    return { ok: false, code: 'unknown', message: 'Camera returned no file path.' }
  }

  try {
    const response = await fetch(src)
    const blob = await response.blob()
    const ext = (photo.format ?? 'jpeg').toLowerCase()
    const mime = blob.type || `image/${ext === 'jpg' ? 'jpeg' : ext}`
    const file = new File([blob], `capture_${Date.now()}.${ext}`, { type: mime })
    return { ok: true, file }
  }
  catch (err) {
    return {
      ok: false,
      code: 'unknown',
      message: err instanceof Error ? err.message : 'Failed to read captured file.',
    }
  }
}

function mapCameraError(err: unknown): MobilePickerError {
  const message = err instanceof Error ? err.message : String(err)
  if (/denied|permission/i.test(message)) {
    return { ok: false, code: 'permission_denied', message }
  }
  if (/cancel|User cancelled/i.test(message)) {
    return { ok: false, code: 'cancelled', message }
  }
  return { ok: false, code: 'unknown', message }
}
