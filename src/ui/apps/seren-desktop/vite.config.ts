import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import unocss from 'unocss/vite'
import { resolve } from 'node:path'

// Resolve host for Tauri dev server
const host = process.env.TAURI_DEV_HOST

export default defineConfig({
  plugins: [vue(), unocss()],
  resolve: {
    alias: {
      // Reuse seren-web source directly
      '@seren/web-pages': resolve(__dirname, '../seren-web/src/pages'),
      '@seren/web-app': resolve(__dirname, '../seren-web/src/App.vue'),
    },
  },
  clearScreen: false,
  server: {
    port: 1420,
    strictPort: true,
    host: host || false,
    hmr: host ? { protocol: 'ws', host, port: 1421 } : undefined,
    watch: { ignored: ['**/src-tauri/**'] },
  },
  build: {
    target: process.env.TAURI_ENV_PLATFORM === 'windows' ? 'chrome105' : 'safari14',
    minify: false,
    sourcemap: !!process.env.TAURI_ENV_DEBUG,
    rollupOptions: {
      external: ['pixi-live2d-display'],
    },
  },
})