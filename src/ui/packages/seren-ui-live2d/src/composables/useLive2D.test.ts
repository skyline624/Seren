import { describe, expect, it } from 'vitest'
import { EMOTION_MAP } from './useLive2D'

describe('EMOTION_MAP', () => {
  it('EmotionMap_ForCommonEnglishEmotions_ShouldMapToExpression', () => {
    expect(EMOTION_MAP.joy?.expression).toBe('happy')
    expect(EMOTION_MAP.angry?.expression).toBe('angry')
    expect(EMOTION_MAP.sad?.expression).toBe('sad')
    expect(EMOTION_MAP.surprise?.expression).toBe('surprised')
    expect(EMOTION_MAP.neutral?.expression).toBe('neutral')
  })

  it('EmotionMap_ForSynonyms_ShouldMapToSameExpression', () => {
    expect(EMOTION_MAP.joy?.expression).toBe(EMOTION_MAP.happy?.expression)
    expect(EMOTION_MAP.anger?.expression).toBe(EMOTION_MAP.angry?.expression)
    expect(EMOTION_MAP.sorrow?.expression).toBe(EMOTION_MAP.sad?.expression)
    expect(EMOTION_MAP.surprise?.expression).toBe(EMOTION_MAP.surprised?.expression)
  })

  it('EmotionMap_ForMotionBased_ShouldMapToGroupAndIndex', () => {
    expect(EMOTION_MAP.wave).toEqual({ group: 'tap body', index: 0 })
    expect(EMOTION_MAP.nod).toEqual({ group: 'tap body', index: 1 })
  })

  it('EmotionMap_ForUnknownEmotion_ShouldBeUndefined', () => {
    expect(EMOTION_MAP.disgust).toBeUndefined()
    expect(EMOTION_MAP.fear).toBeUndefined()
  })
})
