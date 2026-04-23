import { describe, it, expect } from 'vitest'
import {
  DEFAULT_IDLE_CATALOG,
  pickNextIdle,
  type IdleAnimation,
} from './idleAnimationCatalog'

describe('idleAnimationCatalog', () => {
  describe('pickNextIdle', () => {
    it('throws on an empty catalog', () => {
      expect(() => pickNextIdle([], null)).toThrow(/empty/i)
    })

    it('returns the single entry for a single-entry catalog regardless of random', () => {
      const catalog: IdleAnimation[] = [
        { id: 'only', durationMs: 1000, moodWeights: { neutral: 1 } },
      ]
      expect(pickNextIdle(catalog, null, () => 0).id).toBe('only')
      expect(pickNextIdle(catalog, null, () => 0.99).id).toBe('only')
    })

    it('picks deterministically with an injected PRNG', () => {
      const catalog: IdleAnimation[] = [
        { id: 'a', durationMs: 1000, moodWeights: { neutral: 1 } },
        { id: 'b', durationMs: 1000, moodWeights: { neutral: 1 } },
        { id: 'c', durationMs: 1000, moodWeights: { neutral: 1 } },
      ]
      // Total weight = 3. random=0.0 → roll=0 → first entry ('a').
      expect(pickNextIdle(catalog, null, () => 0.0).id).toBe('a')
      // random=0.5 → roll=1.5 → a(-1) → b(-1) selected.
      expect(pickNextIdle(catalog, null, () => 0.5).id).toBe('b')
      // random=0.99 → near total → last entry ('c').
      expect(pickNextIdle(catalog, null, () => 0.99).id).toBe('c')
    })

    it('biases selection by mood weights', () => {
      // b is heavily weighted for joy, a for sad — verify the bias
      // actually influences picks over a large sample.
      const catalog: IdleAnimation[] = [
        { id: 'a', durationMs: 1000, moodWeights: { neutral: 1, joy: 0.1, sad: 5 } },
        { id: 'b', durationMs: 1000, moodWeights: { neutral: 1, joy: 5, sad: 0.1 } },
      ]
      const sampleOf = (mood: string): string[] => {
        let seed = 0
        const rand = (): number => {
          // Tiny LCG for deterministic distribution sampling.
          seed = (seed * 1664525 + 1013904223) & 0xFFFF
          return seed / 0xFFFF
        }
        return Array.from({ length: 1000 }, () => pickNextIdle(catalog, mood, rand).id)
      }
      const joySample = sampleOf('joy')
      const sadSample = sampleOf('sad')
      const joyB = joySample.filter(id => id === 'b').length
      const sadA = sadSample.filter(id => id === 'a').length
      // With 50:1 relative weights, at least 80 % of picks should
      // land on the dominant entry — gives plenty of room for LCG
      // noise without flaking.
      expect(joyB).toBeGreaterThan(800)
      expect(sadA).toBeGreaterThan(800)
    })

    it('falls back to the neutral weight when the current mood has no weight', () => {
      const catalog: IdleAnimation[] = [
        { id: 'neutral-only', durationMs: 1000, moodWeights: { neutral: 1 } },
      ]
      // Mood "mystery" missing → neutral weight applies, not 0.
      expect(pickNextIdle(catalog, 'mystery', () => 0).id).toBe('neutral-only')
    })

    it('falls back to uniform selection when every entry has zero weight for the mood', () => {
      const catalog: IdleAnimation[] = [
        { id: 'a', durationMs: 1000, moodWeights: { joy: 0 } },
        { id: 'b', durationMs: 1000, moodWeights: { joy: 0 } },
      ]
      // With zero-weights the function falls back to uniform via random().
      // random=0 → index 0 ('a'), random=0.6 → index 1 ('b').
      expect(pickNextIdle(catalog, 'joy', () => 0).id).toBe('a')
      expect(pickNextIdle(catalog, 'joy', () => 0.6).id).toBe('b')
    })

    it('never returns undefined for any mood key on the default catalog', () => {
      const moods = ['neutral', 'joy', 'sad', 'anger', 'surprise', 'relaxed', 'unknown_mood']
      for (const mood of moods) {
        const result = pickNextIdle(DEFAULT_IDLE_CATALOG, mood, () => 0.5)
        expect(result).toBeDefined()
        expect(result.id).toBeTruthy()
      }
    })
  })

  describe('DEFAULT_IDLE_CATALOG', () => {
    it('ships the documented seven variants', () => {
      const ids = DEFAULT_IDLE_CATALOG.map(a => a.id).sort()
      expect(ids).toEqual([
        'blink_double', 'breath_deep', 'look_down', 'look_left',
        'look_right', 'look_up', 'stretch_small',
      ])
    })

    it('assigns a non-negative neutral weight to every entry (so the fallback always works)', () => {
      for (const anim of DEFAULT_IDLE_CATALOG) {
        expect(anim.moodWeights.neutral).toBeDefined()
        expect(anim.moodWeights.neutral).toBeGreaterThan(0)
      }
    })
  })
})
