<script setup lang="ts">
import { computed, defineAsyncComponent, ref } from 'vue'
import { useSerenModulesRegistry } from '../stores/modules'
import AppearanceSection from './settings/AppearanceSection.vue'
import ComingSoonSection from './settings/ComingSoonSection.vue'
import ConnectionSection from './settings/ConnectionSection.vue'
import LlmSection from './settings/LlmSection.vue'
import SectionNav from './settings/SectionNav.vue'

// SVG icons inlined so consumers don't need to pull an icon pack.
const ICON_APPEARANCE = `<svg viewBox="0 0 24 24"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10c1.66 0 3-1.34 3-3 0-.78-.29-1.48-.77-2.01a1 1 0 0 1-.23-.65c0-.55.45-1 1-1H17c2.76 0 5-2.24 5-5 0-4.42-4.48-8-10-8Zm-5.5 10a1.5 1.5 0 1 1 0-3 1.5 1.5 0 0 1 0 3Zm3-4a1.5 1.5 0 1 1 0-3 1.5 1.5 0 0 1 0 3Zm5 0a1.5 1.5 0 1 1 0-3 1.5 1.5 0 0 1 0 3Zm3 4a1.5 1.5 0 1 1 0-3 1.5 1.5 0 0 1 0 3Z"/></svg>`
const ICON_LLM = `<svg viewBox="0 0 24 24"><path d="M15 9h-1V7h1a1 1 0 0 0 1-1V5a1 1 0 0 0-1-1h-1V3a1 1 0 0 0-2 0v1h-2V3a1 1 0 0 0-2 0v1H8a1 1 0 0 0-1 1v1a1 1 0 0 0 1 1h1v2H8a2 2 0 0 0-2 2v2H5a1 1 0 0 0 0 2h1v2H5a1 1 0 0 0 0 2h1v1a2 2 0 0 0 2 2h1v1a1 1 0 0 0 2 0v-1h2v1a1 1 0 0 0 2 0v-1h1a2 2 0 0 0 2-2v-1h1a1 1 0 0 0 0-2h-1v-2h1a1 1 0 0 0 0-2h-1v-2a2 2 0 0 0-2-2Zm1 9a1 1 0 0 1-1 1H9a1 1 0 0 1-1-1v-6a1 1 0 0 1 1-1h6a1 1 0 0 1 1 1Zm-6-2h4v-2h-4Z"/></svg>`
const ICON_CHARACTER = `<svg viewBox="0 0 24 24"><path d="M12 6a3 3 0 1 1 0 6 3 3 0 0 1 0-6Zm0-4C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2Zm0 18c-2.03 0-3.9-.67-5.41-1.79.55-1.01 3.19-2.21 5.41-2.21 2.22 0 4.87 1.2 5.41 2.21A7.95 7.95 0 0 1 12 20Zm6.86-3.26c-1.8-2.19-6.15-2.94-6.86-2.94-.71 0-5.06.75-6.86 2.94A7.99 7.99 0 0 1 4 12c0-4.41 3.59-8 8-8s8 3.59 8 8c0 1.81-.61 3.48-1.64 4.82Z"/></svg>`
const ICON_CONNECTION = `<svg viewBox="0 0 24 24"><path d="M12 4c-4.42 0-8 3.58-8 8s3.58 8 8 8 8-3.58 8-8-3.58-8-8-8Zm-1 13.93A8 8 0 0 1 4.07 13H7v2a2 2 0 0 0 2 2v.93Zm6.9-2.54c-.25-.78-1-1.39-1.9-1.39h-1v-3c0-.55-.45-1-1-1H9v-2h2c.55 0 1-.45 1-1V5.07a8 8 0 0 1 5.9 10.32Z"/></svg>`

interface SectionEntry {
  id: string
  labelKey: string
  icon: string
  order: number
  badgeKey?: string
}

// Hardcoded core sections that haven't been extracted into modules yet.
// Order values leave room (10, 20, 40, …) for modules to slot themselves
// in between (e.g. the audio module pins itself at 30 to land between
// connection and llm). The 'voice' section is gone — the audio module
// provides it now.
const CORE_SECTIONS: SectionEntry[] = [
  { id: 'appearance', labelKey: 'settings.nav.appearance', icon: ICON_APPEARANCE, order: 10 },
  { id: 'connection', labelKey: 'settings.nav.connection', icon: ICON_CONNECTION, order: 20 },
  { id: 'llm', labelKey: 'settings.nav.llm', icon: ICON_LLM, order: 40 },
  {
    id: 'character',
    labelKey: 'settings.nav.character',
    icon: ICON_CHARACTER,
    order: 50,
    badgeKey: 'settings.nav.comingSoonBadge',
  },
]

const registry = useSerenModulesRegistry()

const sections = computed<SectionEntry[]>(() => {
  const moduleEntries: SectionEntry[] = registry.settingsTabs.map(tab => ({
    id: tab.id,
    labelKey: tab.labelKey,
    icon: tab.icon,
    order: tab.order,
    badgeKey: tab.badgeKey,
  }))
  return [...CORE_SECTIONS, ...moduleEntries]
    .sort((a, b) => (a.order - b.order) || a.id.localeCompare(b.id))
})

const activeSection = ref<string>('appearance')

// Cache async components per module id so switching tabs doesn't re-trigger
// the dynamic import. Keys are module ids.
const moduleComponentCache = new Map<string, ReturnType<typeof defineAsyncComponent>>()
function resolveModuleComponent(id: string) {
  const cached = moduleComponentCache.get(id)
  if (cached) return cached
  const tab = registry.settingsTabs.find(t => t.id === id)
  if (!tab) return null
  // Cast: SerenModuleSettingsDescriptor.component is typed as
  // `() => Promise<{ default: unknown }>` to keep the contract permissive
  // for module authors. defineAsyncComponent verifies the actual shape
  // at runtime.
  const wrapper = defineAsyncComponent(tab.component as () => Promise<{ default: import('vue').Component }>)
  moduleComponentCache.set(id, wrapper)
  return wrapper
}

const activeModuleComponent = computed(() => {
  const known = ['appearance', 'connection', 'llm', 'character']
  if (known.includes(activeSection.value)) return null
  return resolveModuleComponent(activeSection.value)
})

// Kept for parent template contract (App.vue still binds @open-character-editor).
// The character tab now shows a ComingSoonSection placeholder, so this never fires
// — but removing it would break parent listeners until the persona feature is reactivated.
defineEmits<{
  'open-character-editor': []
}>()
</script>

<template>
  <div class="settings-panel">
    <SectionNav v-model="activeSection" :sections="sections" />

    <div class="settings-panel__content">
      <AppearanceSection v-if="activeSection === 'appearance'" />
      <ConnectionSection v-else-if="activeSection === 'connection'" />
      <LlmSection v-else-if="activeSection === 'llm'" />
      <ComingSoonSection
        v-else-if="activeSection === 'character'"
        title-key="settings.character.comingSoonTitle"
        message-key="settings.character.comingSoonMessage"
      />
      <component :is="activeModuleComponent" v-else-if="activeModuleComponent" />
    </div>
  </div>
</template>

<style scoped>
.settings-panel {
  display: flex;
  gap: 1rem;
  min-height: 360px;
  color: var(--airi-text);
}

.settings-panel__content {
  flex: 1;
  min-width: 0;
  padding: 0 0.25rem 0.5rem;
}

@media (max-width: 540px) {
  .settings-panel {
    flex-direction: column;
    gap: 0.75rem;
  }
}
</style>
