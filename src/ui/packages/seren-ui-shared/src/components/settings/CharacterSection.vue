<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useCharacterStore, type CharacterDto, type CreateCharacterInput } from '../../stores/character'

const store = useCharacterStore()
const active = computed<CharacterDto | null>(() => store.activeCharacter)

// Local draft so the user can edit without touching the live store until
// they hit Save. `null` when no active character; rehydrated every time
// the active character changes (switch via CharacterSelector, refetch, …).
const draft = ref<CreateCharacterInput | null>(null)

function snapshot(src: CharacterDto | null): CreateCharacterInput | null {
  if (!src) return null
  return {
    name: src.name,
    systemPrompt: src.systemPrompt,
    vrmAssetPath: src.vrmAssetPath ?? null,
    voice: src.voice ?? null,
    agentId: src.agentId ?? null,
  }
}

watch(active, (c) => { draft.value = snapshot(c) }, { immediate: true })

const dirty = computed<boolean>(() => {
  if (!active.value || !draft.value) return false
  const a = active.value
  const d = draft.value
  return a.name !== d.name
    || a.systemPrompt !== d.systemPrompt
    || (a.vrmAssetPath ?? null) !== (d.vrmAssetPath ?? null)
    || (a.voice ?? null) !== (d.voice ?? null)
    || (a.agentId ?? null) !== (d.agentId ?? null)
})

const saving = ref(false)

async function save(): Promise<void> {
  if (!active.value || !draft.value || saving.value) return
  saving.value = true
  try {
    await store.update(active.value.id, draft.value)
  }
  finally {
    saving.value = false
  }
}

function revert(): void {
  draft.value = snapshot(active.value)
}

defineEmits<{
  'open-character-editor': []
}>()
</script>

<template>
  <section class="settings-section">
    <h3 class="settings-section__title">{{ $t('settings.character.title') }}</h3>

    <div v-if="!active || !draft" class="settings-field">
      <p class="settings-field__hint">{{ $t('settings.character.none') }}</p>
    </div>

    <template v-else>
      <div class="settings-field">
        <label class="settings-field__label" for="char-name">
          {{ $t('settings.character.name') }}
        </label>
        <input
          id="char-name"
          v-model="draft.name"
          type="text"
          class="settings-field__input"
        >
      </div>

      <div class="settings-field">
        <label class="settings-field__label" for="char-agent">
          {{ $t('settings.character.agentId') }}
        </label>
        <input
          id="char-agent"
          v-model="draft.agentId"
          type="text"
          placeholder="ollama/seren-qwen"
          class="settings-field__input"
        >
      </div>

      <div class="settings-field">
        <label class="settings-field__label" for="char-prompt">
          {{ $t('settings.character.systemPrompt') }}
        </label>
        <textarea
          id="char-prompt"
          v-model="draft.systemPrompt"
          rows="8"
          class="settings-field__input settings-field__textarea"
        />
      </div>

      <div class="settings-field">
        <label class="settings-field__label" for="char-vrm">
          {{ $t('settings.character.vrmAssetPath') }}
        </label>
        <input
          id="char-vrm"
          v-model="draft.vrmAssetPath"
          type="text"
          placeholder="/avatars/vrm/avatar.vrm"
          class="settings-field__input"
        >
      </div>

      <p v-if="store.error" class="settings-field__hint" style="color: oklch(0.7 0.15 25);">
        {{ store.error }}
      </p>
    </template>

    <div class="settings-section__actions">
      <button
        v-if="active"
        type="button"
        class="settings-section__btn settings-section__btn--primary"
        :disabled="!dirty || saving"
        @click="save"
      >
        {{ saving ? $t('settings.character.saving') : $t('settings.character.save') }}
      </button>
      <button
        v-if="active"
        type="button"
        class="settings-section__btn"
        :disabled="!dirty || saving"
        @click="revert"
      >
        {{ $t('settings.character.cancel') }}
      </button>
      <button type="button" class="settings-section__btn" @click="$emit('open-character-editor')">
        {{ $t('settings.character.manage') }}
      </button>
    </div>
  </section>
</template>

<style scoped>
@import './section-common.css';

.settings-field__textarea {
  resize: vertical;
  min-height: 120px;
  font-family: ui-monospace, monospace;
  line-height: 1.4;
}

.settings-section__btn--primary {
  background: var(--airi-accent);
  color: oklch(0.12 0.02 var(--seren-hue));
  border-color: transparent;
}

.settings-section__btn--primary:hover:not(:disabled) {
  background: oklch(0.78 0.13 var(--seren-hue));
  color: oklch(0.12 0.02 var(--seren-hue));
}

.settings-section__btn:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}
</style>
