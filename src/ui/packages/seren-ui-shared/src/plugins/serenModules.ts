import type { SerenModuleDefinition } from '@seren/sdk'
import type { App, InjectionKey } from 'vue'
import { createChatHookRegistry, useSerenModulesRegistry } from '../stores/modules'

/** Options accepted by the modules plugin. */
export interface SerenModulesPluginOptions {
  /** Modules to register at install time, in priority order. */
  modules: SerenModuleDefinition[]
}

/**
 * Vue plugin that registers a set of Seren UI modules and runs each
 * module's optional `install` hook once. Pinia must be installed first
 * (the registry is a Pinia store).
 *
 * @example
 * ```ts
 * import audioModule from '@seren/module-audio'
 *
 * app.use(pinia)
 * app.use(serenModulesPlugin, { modules: [audioModule] })
 * ```
 */
export const serenModulesPlugin = {
  install(app: App, options: SerenModulesPluginOptions) {
    if (!options || !Array.isArray(options.modules)) {
      throw new Error(
        'serenModulesPlugin: options.modules is required and must be an array of SerenModuleDefinition.',
      )
    }

    const registry = useSerenModulesRegistry()
    const chatHooks = createChatHookRegistry()

    for (const module of options.modules) {
      registry.register(module)
      module.install?.({ chatHooks })
    }

    // Expose the chat-hook registry through provide/inject so consumers
    // (chat composables) can read it without going through a Pinia
    // store. The shared symbol is exported from the same module so both
    // sides agree on the key.
    app.provide(serenChatHooksKey, chatHooks)
  },
}

/** Shared injection key for the chat hook registry. */
export const serenChatHooksKey = Symbol('seren.chatHooks') as InjectionKey<ReturnType<typeof createChatHookRegistry>>
