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
      // pixi-live2d-display MUST NOT be bundled: it has peerDependencies on
      // the individual @pixi/* packages and ships its own PIXI internals. If
      // we bundle it here, the consumer's pixi.js umbrella ends up with a
      // different class identity than the one the Live2DModel uses, so the
      // model is created on a foreign scene graph and never reaches the GPU.
      external: [
        'vue',
        'pixi.js',
        'pixi-live2d-display',
        /^pixi-live2d-display\//,
      ],
    },
    sourcemap: true,
    minify: false,
  },
})
