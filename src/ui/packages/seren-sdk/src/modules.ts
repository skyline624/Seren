import type { ChatHookRegistry } from './chat-hooks'

/**
 * Compile-time UI module contract. A Seren UI module is a self-contained
 * bundle that ships an optional Settings tab, optional WebSocket / chat
 * hooks, and optional locale messages. Modules are registered explicitly
 * by each app at build time — no runtime discovery, no manifest scan.
 *
 * The shape mirrors the C# `ISerenModule` contract on the server: the
 * `id` field is the single source of truth and matches the server
 * module's id when there's a server-side counterpart.
 */
export interface SerenModuleDefinition {
  /** Stable, kebab-case identifier. Matches the server-side module id when paired. */
  readonly id: string

  /** SemVer string surfaced for diagnostics (e.g. dev tools, telemetry). */
  readonly version: string

  /**
   * Optional settings tab. The component is loaded lazily via dynamic
   * import to keep the initial bundle small.
   */
  settings?: SerenModuleSettingsDescriptor

  /**
   * Optional locale messages. Merged into the global i18n catalog under
   * the namespace `modules.{id}.*` to avoid colliding with the core
   * `settings.*` / `chat.*` keys.
   */
  locales?: SerenModuleLocaleBundle

  /**
   * Optional install hook called once during app bootstrap. Use it to
   * register chat hooks, subscribe to WebSocket events, or read/write
   * Pinia stores. The returned cleanup function (if any) is invoked on
   * teardown — useful for tests or future hot-reload support.
   */
  install?: (context: SerenModuleContext) => void | (() => void)
}

/** Settings-tab descriptor. */
export interface SerenModuleSettingsDescriptor {
  /** i18n key for the tab label, e.g. <c>"modules.audio.title"</c>. */
  labelKey: string

  /** Raw inline SVG markup for the nav icon (consumed via `v-html`). */
  icon: string

  /**
   * Lazy component loader. Returns a Promise resolving to a module whose
   * default export is a Vue component. Typed loosely on purpose: vue-tsc
   * trips over the strict `Component` type when a module package wires a
   * native `import('./X.vue')` here, and the loader's only consumer is
   * Vue's `defineAsyncComponent` which accepts any thenable shape.
   */
  component: () => Promise<{ default: unknown }>

  /** Sort order in the settings nav (default 100). Lower = earlier. */
  order?: number

  /** Optional badge i18n key (e.g. <c>"settings.nav.comingSoonBadge"</c>). */
  badgeKey?: string
}

/** Per-locale messages shipped by a module. */
export interface SerenModuleLocaleBundle {
  en: Record<string, unknown>
  fr: Record<string, unknown>
}

/**
 * Context passed to <see cref="SerenModuleDefinition.install" />.
 * Surface kept narrow on purpose — modules that need more should consume
 * Pinia stores or composables directly rather than expand this object.
 */
export interface SerenModuleContext {
  /**
   * Chat hook registry — see <c>chat-hooks.ts</c>. Modules that don't
   * need to instrument the chat pipeline can ignore this.
   */
  readonly chatHooks: ChatHookRegistry
}

/**
 * Identity helper that doubles as a type guard. Authors call it inside
 * each module package's entry point to get full IntelliSense and a
 * stable, immutable definition object.
 */
export function defineSerenModule(definition: SerenModuleDefinition): SerenModuleDefinition {
  return definition
}

// Re-export so callers don't have to import from chat-hooks.ts as well.
export type { ChatHookRegistry, ChatHooks } from './chat-hooks'
