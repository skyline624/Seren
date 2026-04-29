import type { AudioConstraints } from './types'

/**
 * Sentinel meaning "let the OS pick the default device" — the same
 * string the voice store persists when the user hasn't picked a
 * specific microphone.
 */
const DEFAULT_DEVICE_SENTINEL = 'default'

/**
 * Builds the <c>MediaStreamConstraints</c> object passed to
 * <c>navigator.mediaDevices.getUserMedia</c>. Centralises the
 * deviceId / browser-filter logic so both strategies (VAD + PTT) use
 * the exact same acquisition rules (DRY).
 */
export function buildAudioConstraints(
  deviceId: string | undefined,
  filters: AudioConstraints | undefined,
): MediaStreamConstraints {
  const audio: MediaTrackConstraints = {}

  if (deviceId && deviceId !== DEFAULT_DEVICE_SENTINEL) {
    audio.deviceId = { exact: deviceId }
  }

  if (filters?.noiseSuppression !== undefined) {
    audio.noiseSuppression = filters.noiseSuppression
  }
  if (filters?.echoCancellation !== undefined) {
    audio.echoCancellation = filters.echoCancellation
  }
  if (filters?.autoGainControl !== undefined) {
    audio.autoGainControl = filters.autoGainControl
  }

  return { audio: Object.keys(audio).length > 0 ? audio : true }
}

/**
 * Acquires a MediaStream with the given device + filter constraints.
 * Wraps <c>getUserMedia</c> so callers don't have to think about the
 * constraint shape. Throws the underlying DOMException unchanged so
 * callers can surface permission errors verbatim.
 */
export async function acquireAudioStream(
  deviceId: string | undefined,
  filters: AudioConstraints | undefined,
): Promise<MediaStream> {
  const constraints = buildAudioConstraints(deviceId, filters)
  return navigator.mediaDevices.getUserMedia(constraints)
}

/**
 * Stops every track of a MediaStream. Best-effort — never throws.
 * Safe to call multiple times.
 */
export function releaseStream(stream: MediaStream | null): void {
  if (!stream) {
    return
  }
  for (const track of stream.getTracks()) {
    try {
      track.stop()
    }
    catch {
      // best-effort cleanup; ignore
    }
  }
}
