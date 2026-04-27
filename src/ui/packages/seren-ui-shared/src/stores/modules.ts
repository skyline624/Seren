import type {
  ChatHookRegistry,
  ChatHooks,
  SerenModuleDefinition,
} from '@seren/sdk'
import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

/**
 * Materialised view of a module's settings descriptor — same shape as
 * `SerenModuleDefinition.settings` but with the parent module's id
 * pinned so the SettingsPanel can render the registry as a flat list.
 */
export interface SerenModuleSettingsTab {
  /** Module id (e.g. <c>"audio"</c>). Used as the unique tab identifier. */
  id: string
  labelKey: string
  icon: string
  component: () => Promise<{ default: unknown }>
  /** Lower = earlier in the nav. Defaults to 100 in the registry. */
  order: number
  badgeKey?: string
}

/**
 * Pinia store collecting every registered Seren UI module. Apps register
 * their modules once at boot via `useSerenModulesRegistry().register(...)`
 * and consumers read the derived collections (settings tabs, locale
 * bundles) reactively.
 */
export const useSerenModulesRegistry = defineStore('seren/modules-registry', () => {
  const modules = ref(new Map<string, SerenModuleDefinition>())

  /**
   * Registers a module by id. A second call with the same id replaces
   * the first — useful for hot-reload scenarios but normal apps should
   * call this exactly once per module during bootstrap.
   */
  function register(definition: SerenModuleDefinition): void {
    modules.value.set(definition.id, definition)
  }

  /**
   * Settings tabs sorted by `order` (ascending) then by `id` (stable
   * tiebreaker). Returns a fresh array on every access — cheap given
   * registries are small.
   */
  const settingsTabs = computed<SerenModuleSettingsTab[]>(() => {
    const tabs: SerenModuleSettingsTab[] = []
    for (const def of modules.value.values()) {
      if (!def.settings) continue
      tabs.push({
        id: def.id,
        labelKey: def.settings.labelKey,
        icon: def.settings.icon,
        component: def.settings.component,
        order: def.settings.order ?? 100,
        badgeKey: def.settings.badgeKey,
      })
    }
    tabs.sort((a, b) => (a.order - b.order) || a.id.localeCompare(b.id))
    return tabs
  })

  /** Snapshot of module ids — useful for diagnostics / dev tools. */
  const ids = computed(() => Array.from(modules.value.keys()).sort())

  return {
    register,
    settingsTabs,
    ids,
  }
})

/**
 * Default ChatHookRegistry implementation backed by a Map. Created once
 * by the modules plugin and shared with every module's `install` hook.
 */
export function createChatHookRegistry(): ChatHookRegistry {
  const bundles = new Map<string, ChatHooks>()

  function register(moduleId: string, hooks: ChatHooks): () => void {
    bundles.set(moduleId, hooks)
    return () => {
      bundles.delete(moduleId)
    }
  }

  return {
    register,
    get entries() {
      return Array.from(bundles.entries()) as ReadonlyArray<readonly [string, ChatHooks]>
    },
  }
}
