import { onMounted, onUnmounted, ref, type Ref } from 'vue'

/**
 * Reactive view over <c>navigator.mediaDevices.enumerateDevices()</c>
 * filtered to inputs (<c>kind === 'audioinput'</c>). Subscribes to
 * <c>devicechange</c> so plugging or unplugging a USB headset
 * refreshes the list without user action.
 *
 * <c>hasPermission</c> is a derived signal: when the browser hasn't
 * been granted microphone access yet, <c>enumerateDevices</c> still
 * returns the device list but with empty <c>label</c> values — the
 * UI uses this to surface a "Allow access first" hint instead of an
 * unusable dropdown.
 */
export interface UseAudioDevicesApi {
  devices: Ref<MediaDeviceInfo[]>
  isLoading: Ref<boolean>
  hasPermission: Ref<boolean>
  refresh: () => Promise<void>
}

export function useAudioDevices(): UseAudioDevicesApi {
  const devices = ref<MediaDeviceInfo[]>([])
  const isLoading = ref(false)
  const hasPermission = ref(false)

  async function refresh(): Promise<void> {
    if (typeof navigator === 'undefined' || !navigator.mediaDevices?.enumerateDevices) {
      devices.value = []
      hasPermission.value = false
      return
    }

    isLoading.value = true
    try {
      const list = await navigator.mediaDevices.enumerateDevices()
      const inputs = list.filter(d => d.kind === 'audioinput')
      devices.value = inputs
      // Labels are populated only after the user grants mic permission.
      // An input device with a non-empty label proves we have it.
      hasPermission.value = inputs.some(d => d.label.length > 0)
    }
    catch {
      devices.value = []
      hasPermission.value = false
    }
    finally {
      isLoading.value = false
    }
  }

  function handleDeviceChange(): void {
    void refresh()
  }

  onMounted(() => {
    void refresh()
    if (typeof navigator !== 'undefined' && navigator.mediaDevices) {
      navigator.mediaDevices.addEventListener('devicechange', handleDeviceChange)
    }
  })

  onUnmounted(() => {
    if (typeof navigator !== 'undefined' && navigator.mediaDevices) {
      navigator.mediaDevices.removeEventListener('devicechange', handleDeviceChange)
    }
  })

  return { devices, isLoading, hasPermission, refresh }
}
