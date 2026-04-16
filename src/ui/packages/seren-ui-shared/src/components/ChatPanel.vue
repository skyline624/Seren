<script setup lang="ts">
import { ref, nextTick, watch } from 'vue'
import { useChatStore } from '../stores/chat'

const store = useChatStore()
const inputText = ref('')
const messagesContainer = ref<HTMLElement>()

function handleSend(): void {
  const text = inputText.value.trim()
  if (!text || store.isStreaming) return
  store.sendMessage(text)
  inputText.value = ''
  nextTick(scrollToBottom)
}

function handleKeydown(e: KeyboardEvent): void {
  if (e.key === 'Enter' && !e.shiftKey) {
    e.preventDefault()
    handleSend()
  }
}

function scrollToBottom(): void {
  if (messagesContainer.value) {
    messagesContainer.value.scrollTop = messagesContainer.value.scrollHeight
  }
}

// Auto-scroll when new messages arrive
watch(() => store.messages.length, () => nextTick(scrollToBottom))
watch(() => store.currentAssistantContent, () => nextTick(scrollToBottom))
</script>

<template>
  <div class="chat-panel">
    <div ref="messagesContainer" class="chat-messages">
      <div
        v-for="msg in store.messages"
        :key="msg.id"
        :class="['chat-message', `chat-message--${msg.role}`]"
      >
        <span class="chat-message__role">{{ msg.role === 'user' ? 'You' : 'Seren' }}</span>
        <span class="chat-message__content">{{ msg.content }}</span>
      </div>
      <!-- Streaming assistant message -->
      <div v-if="store.isStreaming && store.currentAssistantContent" class="chat-message chat-message--assistant">
        <span class="chat-message__role">Seren</span>
        <span class="chat-message__content streaming">{{ store.currentAssistantContent }}&#9612;</span>
      </div>
    </div>
    <div class="chat-input">
      <textarea
        v-model="inputText"
        :disabled="store.isStreaming"
        placeholder="Type a message..."
        rows="1"
        @keydown="handleKeydown"
      />
      <button :disabled="!inputText.trim() || store.isStreaming" @click="handleSend">
        Send
      </button>
    </div>
  </div>
</template>

<style scoped>
.chat-panel {
  display: flex;
  flex-direction: column;
  height: 100%;
  max-height: 100vh;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  overflow: hidden;
  background: #fff;
}

.chat-messages {
  flex: 1;
  overflow-y: auto;
  padding: 1rem;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.chat-message {
  padding: 0.5rem 0.75rem;
  border-radius: 6px;
  max-width: 80%;
  word-wrap: break-word;
}

.chat-message--user {
  align-self: flex-end;
  background: #3b82f6;
  color: #fff;
}

.chat-message--assistant {
  align-self: flex-start;
  background: #f1f5f9;
  color: #1e293b;
}

.chat-message__role {
  font-size: 0.75rem;
  font-weight: 600;
  text-transform: uppercase;
  opacity: 0.7;
  display: block;
  margin-bottom: 0.25rem;
}

.chat-message--user .chat-message__role {
  color: #dbeafe;
}

.chat-message--assistant .chat-message__role {
  color: #64748b;
}

.streaming {
  font-style: italic;
}

.chat-input {
  display: flex;
  gap: 0.5rem;
  padding: 0.75rem;
  border-top: 1px solid #e2e8f0;
  background: #f8fafc;
}

.chat-input textarea {
  flex: 1;
  padding: 0.5rem;
  border: 1px solid #cbd5e1;
  border-radius: 4px;
  resize: none;
  font-family: inherit;
  font-size: 0.875rem;
}

.chat-input textarea:focus {
  outline: none;
  border-color: #3b82f6;
}

.chat-input button {
  padding: 0.5rem 1rem;
  background: #3b82f6;
  color: #fff;
  border: none;
  border-radius: 4px;
  cursor: pointer;
  font-size: 0.875rem;
  font-weight: 500;
}

.chat-input button:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.chat-input button:not(:disabled):hover {
  background: #2563eb;
}
</style>