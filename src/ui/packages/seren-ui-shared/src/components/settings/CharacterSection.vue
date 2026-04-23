<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useCharacterStore, type CharacterDto, type CreateCharacterInput } from '../../stores/character'

const store = useCharacterStore()
const active = computed<CharacterDto | null>(() => store.activeCharacter)

// ── Character Card v3 import ───────────────────────────────────────────
const fileInput = ref<HTMLInputElement | null>(null)
const importing = ref(false)

function openFilePicker(): void {
  fileInput.value?.click()
}

async function handleFileChosen(event: Event): Promise<void> {
  const target = event.target as HTMLInputElement
  const file = target.files?.[0]
  if (!file) return
  importing.value = true
  try {
    await store.importCard(file, /* activate: */ false)
  }
  finally {
    importing.value = false
    target.value = '' // allow re-selecting the same file
  }
}

const avatarUrl = computed(() => store.avatarUrl(active.value))

const importToastClass = computed(() =>
  store.lastImport?.status === 'ok'
    ? 'settings-import-toast settings-import-toast--ok'
    : 'settings-import-toast settings-import-toast--error',
)

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

    <!-- Character Card v3 import — read-only display of imported fields -->
    <div v-if="active" class="settings-imported">
      <div v-if="avatarUrl" class="settings-imported__avatar">
        <img :src="avatarUrl" :alt="active.name">
      </div>
      <div class="settings-imported__fields">
        <p v-if="active.greeting" class="settings-imported__greeting">
          <strong>{{ $t('settings.character.greeting') }}:</strong> {{ active.greeting }}
        </p>
        <p v-if="active.description" class="settings-imported__description">
          {{ active.description }}
        </p>
        <ul v-if="active.tags && active.tags.length" class="settings-imported__tags">
          <li v-for="tag in active.tags" :key="tag">{{ tag }}</li>
        </ul>
      </div>
    </div>

    <!-- Hidden file input driven by the Import button -->
    <input
      ref="fileInput"
      type="file"
      accept=".png,.apng,.json,application/json,image/png,image/apng"
      hidden
      @change="handleFileChosen"
    >

    <!-- Import status toast — success or typed error -->
    <div v-if="store.lastImport" :class="importToastClass" @click="store.lastImport = null">
      <template v-if="store.lastImport.status === 'ok'">
        {{ $t('characters.import.success', { name: store.lastImport.characterName }) }}
        <span v-for="w in store.lastImport.warnings" :key="w" class="settings-import-toast__warning">
          · {{ $t(`characters.import.warnings.${w}`) }}
        </span>
      </template>
      <template v-else>
        {{ $t(`characters.import.errors.${store.lastImport.errorCode}`) }}
      </template>
    </div>

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
      <button
        type="button"
        class="settings-section__btn"
        :disabled="importing"
        @click="openFilePicker"
      >
        {{ importing ? $t('characters.import.importing') : $t('characters.import.button') }}
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

/* ── Imported CCv3 metadata display (read-only in v1) ───────────── */
.settings-imported {
  display: flex;
  gap: 0.75rem;
  margin: 0.5rem 0;
  padding: 0.5rem;
  background: oklch(0.74 0.127 var(--seren-hue) / 0.06);
  border-radius: 8px;
}

.settings-imported__avatar img {
  width: 72px;
  height: 96px;
  object-fit: cover;
  border-radius: 4px;
  display: block;
}

.settings-imported__fields {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
  min-width: 0;
}

.settings-imported__greeting {
  font-size: 0.8rem;
  color: var(--airi-text-muted);
  margin: 0;
  font-style: italic;
}

.settings-imported__description {
  font-size: 0.8rem;
  margin: 0;
  line-height: 1.4;
  color: var(--airi-text-muted);
}

.settings-imported__tags {
  display: flex;
  flex-wrap: wrap;
  gap: 0.25rem;
  margin: 0;
  padding: 0;
  list-style: none;
}

.settings-imported__tags li {
  padding: 0.1rem 0.5rem;
  font-size: 0.7rem;
  border-radius: 9999px;
  background: oklch(0.74 0.127 var(--seren-hue) / 0.15);
  color: var(--airi-text);
}

/* Import toast — reused for both success and error via modifier. */
.settings-import-toast {
  margin: 0.5rem 0;
  padding: 0.4rem 0.75rem;
  font-size: 0.75rem;
  border-radius: 8px;
  cursor: pointer;
  line-height: 1.4;
}

.settings-import-toast--ok {
  background: oklch(0.7 0.12 140 / 0.18);
  color: oklch(0.85 0.12 140);
}

.settings-import-toast--error {
  background: oklch(0.55 0.15 25 / 0.2);
  color: oklch(0.85 0.1 25);
}

.settings-import-toast__warning {
  display: block;
  font-size: 0.7rem;
  opacity: 0.8;
  margin-top: 0.15rem;
}
</style>
