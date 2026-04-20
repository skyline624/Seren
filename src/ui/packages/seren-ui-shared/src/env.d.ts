/// <reference types="vite/client" />

declare module '*.vue' {
  import type { DefineComponent } from 'vue'
  const component: DefineComponent<object, object, unknown>
  export default component
}

// CSS side-effect imports (e.g. `import './section-common.css'` in scoped style blocks
// via `@import`) — not strictly needed for `.css` files next to components but kept
// for symmetry with how consumers pull our styles.
declare module '*.css'

// vue-i18n augments `ComponentCustomProperties` with `$t` when its plugin is installed
// in the host app (done by seren-web/main.ts). We reflect the same augmentation here so
// vue-tsc is happy when it sees `$t(...)` inside our templates at build time.
declare module 'vue' {
  interface ComponentCustomProperties {
    $t: (key: string, values?: Record<string, unknown>) => string
  }
}
export {}