// Event type name constants mirroring C# Seren.Contracts.Events.EventTypes.

export const EventTypes = {
  TransportHello: 'transport:hello',
  TransportHeartbeat: 'transport:connection:heartbeat',
  ModuleAuthenticate: 'module:authenticate',
  ModuleAuthenticated: 'module:authenticated',
  ModuleAnnounce: 'module:announce',
  ModuleAnnounced: 'module:announced',
  ModuleDeAnnounced: 'module:de-announced',
  RegistryModulesSync: 'registry:modules:sync',
  InputText: 'input:text',
  InputVoice: 'input:voice',
  OutputChatChunk: 'output:chat:chunk',
  OutputChatEnd: 'output:chat:end',
  OutputChatThinkingStart: 'output:chat:thinking:start',
  OutputChatThinkingEnd: 'output:chat:thinking:end',
  AudioPlaybackChunk: 'audio:playback:chunk',
  AudioLipsyncFrame: 'audio:lipsync:frame',
  AvatarEmotion: 'avatar:emotion',
  AvatarAction: 'avatar:action',
  Error: 'error',
} as const

export type EventTypeName = (typeof EventTypes)[keyof typeof EventTypes]