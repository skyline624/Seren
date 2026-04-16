import type { CapacitorConfig } from '@capacitor/cli'

const config: CapacitorConfig = {
  appId: 'com.seren.mobile',
  appName: 'Seren',
  webDir: 'dist',
  server: {
    // For development, proxy to local hub
    url: 'http://localhost:5174',
    cleartext: true,
  },
  plugins: {
    SplashScreen: {
      launchAutoHide: false,
      backgroundColor: '#0f172a',
      showSpinner: false,
    },
  },
}

export default config