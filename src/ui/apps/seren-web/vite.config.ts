import vue from '@vitejs/plugin-vue'
import unocss from 'unocss/vite'
import { defineConfig } from 'vite'
import { VitePWA } from 'vite-plugin-pwa'

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
    unocss(),
    VitePWA({
      registerType: 'autoUpdate',
      includeAssets: ['favicon.svg', 'robots.txt'],
      manifest: {
        name: 'Seren',
        short_name: 'Seren',
        description: 'Seren — your embodied AI companion',
        theme_color: '#0f172a',
        background_color: '#0f172a',
        display: 'standalone',
        start_url: '/',
        icons: [
          {
            src: 'pwa-192x192.png',
            sizes: '192x192',
            type: 'image/png',
          },
          {
            src: 'pwa-512x512.png',
            sizes: '512x512',
            type: 'image/png',
          },
          {
            src: 'pwa-512x512.png',
            sizes: '512x512',
            type: 'image/png',
            purpose: 'maskable',
          },
        ],
      },
      workbox: {
        globPatterns: ['**/*.{js,css,html,svg,png,ico,webmanifest}'],
        // Bump the precache size limit above the default 2 MiB so the
        // Hiyori Live2D textures (texture_01.png ≈ 2.5 MB) can be
        // precached and served offline.
        maximumFileSizeToCacheInBytes: 10 * 1024 * 1024,
        runtimeCaching: [
          {
            urlPattern: /\.(?:vrm|glb|gltf|moc3|model3\.json)$/i,
            handler: 'CacheFirst',
            options: {
              cacheName: 'seren-avatar-assets',
              expiration: {
                maxEntries: 20,
                maxAgeSeconds: 60 * 60 * 24 * 30,
              },
            },
          },
        ],
        navigateFallbackDenylist: [/^\/health/, /^\/ws/],
      },
      devOptions: {
        enabled: false,
      },
    }),
  ],
  optimizeDeps: {
    // pixi-live2d-display ships ES modules with subpath exports (/cubism4)
    // and peer-depends on @pixi/*. Pre-bundle both the umbrella and the
    // cubism4 subpath so Vite can resolve the dynamic import at runtime.
    include: ['pixi-live2d-display', 'pixi-live2d-display/cubism4'],
  },
  server: {
    port: 5173,
    proxy: {
      '/ws': {
        target: 'ws://localhost:5000',
        ws: true,
      },
      '/health': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
    },
  },
})