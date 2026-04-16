import { defineConfig, presetUno, presetAttributify } from 'unocss'

export default defineConfig({
  presets: [
    presetUno(),
    presetAttributify(),
  ],
  theme: {
    colors: {
      seren: {
        bg: '#121212',
        surface: 'rgba(10, 30, 40, 0.7)',
        'surface-border': 'rgba(100, 180, 200, 0.2)',
        'surface-light': 'rgba(20, 40, 50, 0.6)',
        text: '#e2e8f0',
        'text-muted': '#94a3b8',
        primary: '#0d9488',
        'primary-light': '#14b8a6',
        accent: '#06b6d4',
        input: 'rgba(15, 23, 42, 0.6)',
        error: '#fca5a5',
      },
    },
  },
})
