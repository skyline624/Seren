import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { resolve } from 'node:path'

export default defineConfig({
  plugins: [
    vue({
      template: {
        compilerOptions: {
          // TresJS components (Tres*) and `primitive` are rendered by TresJS's
          // custom renderer inside <TresCanvas>, not resolved as Vue components.
          // TresCanvas is a real imported Vue component — only mark the
          // children (Three.js object proxies) as custom elements.
          isCustomElement: (tag: string) =>
            (tag.startsWith('Tres') && tag !== 'TresCanvas') || tag === 'primitive',
        },
      },
    }),
  ],
  build: {
    lib: {
      entry: resolve(__dirname, 'src/index.ts'),
      formats: ['es'],
      fileName: () => 'index.mjs',
    },
    rollupOptions: {
      external: ['vue', 'three', '@pixiv/three-vrm', '@tresjs/core'],
    },
    sourcemap: true,
    minify: false,
  },
})