export {
  Client,
  type ClientOptions,
  type ClientStatus,
  type WebSocketFactory,
} from './Client'
export { EventTypes, type EventTypeName } from './types/event-types'
export type {
  WebSocketEnvelope,
  EventMetadata,
  EventIdentity,
  ModuleIdentityDto,
  AnnouncePayload,
  AnnouncedPayload,
  HeartbeatPayload,
  ErrorPayload,
  TextInputPayload,
  VoiceInputPayload,
  ChatChunkPayload,
  ChatEndPayload,
  AudioPlaybackPayload,
  LipsyncFramePayload,
  AvatarEmotionPayload,
  AvatarActionPayload,
  ChatHistoryRequestPayload,
  ChatHistoryItemPayload,
  ChatHistoryEndPayload,
  ChatClearedPayload,
} from './types/events'
export { generateId } from './utils/generate-id'