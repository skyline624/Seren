import { describe, expect, it } from 'vitest'
import { resolveErrorKey } from './ChatErrorDialog.vue'

/**
 * Unit tests for the pure error-code → i18n-key resolver used by
 * `ChatErrorDialog.vue`. Exercising the component's DOM interactions
 * would require a DOM environment (jsdom / happy-dom) that the repo
 * doesn't currently depend on — so we extract and test the only branch
 * that genuinely needs coverage: the code → key mapping.
 *
 * The button-click paths (`retryLastMessage`, `lastError = null`,
 * `emit('open-settings')`) are trivially wired and covered elsewhere :
 * `retryLastMessage` has its own store test in `chat.test.ts`;
 * dismiss is a one-liner; open-settings is a declarative emit.
 */
describe('resolveErrorKey', () => {
  it('maps stream_idle_timeout (pipeline-prefixed) to idle_timeout key', () => {
    expect(resolveErrorKey('stream_idle_timeout')).toBe('chat.error.codes.idle_timeout')
  })

  it('maps bare idle_timeout to idle_timeout key', () => {
    expect(resolveErrorKey('idle_timeout')).toBe('chat.error.codes.idle_timeout')
  })

  it('maps stream_total_timeout to total_timeout key', () => {
    expect(resolveErrorKey('stream_total_timeout')).toBe('chat.error.codes.total_timeout')
  })

  it('maps bare total_timeout to total_timeout key', () => {
    expect(resolveErrorKey('total_timeout')).toBe('chat.error.codes.total_timeout')
  })

  it.each(['401', 'unauthorized', 'auth_required', 'auth_failed'])(
    'maps auth-family code %s to auth key',
    (code) => {
      expect(resolveErrorKey(code)).toBe('chat.error.codes.auth')
    },
  )

  it.each(['404', 'model_not_found', 'resource_not_found'])(
    'maps not-found-family code %s to model_not_found key',
    (code) => {
      expect(resolveErrorKey(code)).toBe('chat.error.codes.model_not_found')
    },
  )

  it('falls back to unknown for unrecognized codes', () => {
    expect(resolveErrorKey('some_future_code')).toBe('chat.error.codes.unknown')
  })

  it('falls back to unknown when code is missing', () => {
    expect(resolveErrorKey(undefined)).toBe('chat.error.codes.unknown')
    expect(resolveErrorKey(null)).toBe('chat.error.codes.unknown')
    expect(resolveErrorKey('')).toBe('chat.error.codes.unknown')
  })
})
