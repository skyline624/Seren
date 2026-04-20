import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { resolve } from 'node:path'

export default defineConfig({
  plugins: [vue()],
  build: {
    lib: {
      entry: resolve(__dirname, 'src/index.ts'),
      formats: ['es'],
      fileName: () => 'index.mjs',
    },
    rollupOptions: {
      external: ['vue', 'pinia', 'vue-i18n', '@seren/sdk', '@seren/ui-three', '@seren/ui-live2d', 'three', 'pixi.js'],
    },
    sourcemap: true,
    minify: false,
  },
})
