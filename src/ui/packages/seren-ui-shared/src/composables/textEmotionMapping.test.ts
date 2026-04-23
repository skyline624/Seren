import { describe, it, expect } from 'vitest'
import { mapClassifierPredictionsToSeren } from './textEmotionMapping'

describe('mapClassifierPredictionsToSeren', () => {
  it('maps known labels directly to the Seren vocabulary', () => {
    expect(mapClassifierPredictionsToSeren([{ label: 'sadness', score: 0.9 }], 0.6)).toBe('sad')
    expect(mapClassifierPredictionsToSeren([{ label: 'joy', score: 0.9 }], 0.6)).toBe('joy')
    expect(mapClassifierPredictionsToSeren([{ label: 'anger', score: 0.9 }], 0.6)).toBe('anger')
    expect(mapClassifierPredictionsToSeren([{ label: 'surprise', score: 0.9 }], 0.6)).toBe('surprise')
    expect(mapClassifierPredictionsToSeren([{ label: 'neutral', score: 0.9 }], 0.6)).toBe('neutral')
  })

  it('maps alternate label spellings (love → joy, fear → surprise, disgust → anger)', () => {
    expect(mapClassifierPredictionsToSeren([{ label: 'love', score: 0.9 }], 0.6)).toBe('joy')
    expect(mapClassifierPredictionsToSeren([{ label: 'fear', score: 0.9 }], 0.6)).toBe('surprise')
    expect(mapClassifierPredictionsToSeren([{ label: 'disgust', score: 0.9 }], 0.6)).toBe('anger')
  })

  it('is case-insensitive on labels', () => {
    expect(mapClassifierPredictionsToSeren([{ label: 'JOY', score: 0.9 }], 0.6)).toBe('joy')
    expect(mapClassifierPredictionsToSeren([{ label: 'Sadness', score: 0.9 }], 0.6)).toBe('sad')
  })

  it('returns neutral when the top prediction is below threshold', () => {
    expect(mapClassifierPredictionsToSeren([{ label: 'joy', score: 0.4 }], 0.6)).toBe('neutral')
  })

  it('returns neutral for unknown labels (no silent mis-mapping)', () => {
    expect(
      mapClassifierPredictionsToSeren([{ label: 'contemplative', score: 0.9 }], 0.6),
    ).toBe('neutral')
  })

  it('returns neutral on empty predictions', () => {
    expect(mapClassifierPredictionsToSeren([], 0.6)).toBe('neutral')
  })

  it('returns neutral when confidence threshold is NaN', () => {
    expect(
      mapClassifierPredictionsToSeren([{ label: 'joy', score: 0.9 }], Number.NaN),
    ).toBe('neutral')
  })

  it('stops scanning past the first below-threshold prediction', () => {
    // Input is sorted desc by score. Once we hit one below threshold,
    // the rest are below too — no point continuing.
    const predictions = [
      { label: 'nonsense', score: 0.5 },
      { label: 'joy', score: 0.4 }, // below threshold even though mappable
    ]
    expect(mapClassifierPredictionsToSeren(predictions, 0.6)).toBe('neutral')
  })

  it('walks past unknown labels to the first mappable one that meets threshold', () => {
    const predictions = [
      { label: 'mystery', score: 0.9 },
      { label: 'joy', score: 0.8 },
    ]
    expect(mapClassifierPredictionsToSeren(predictions, 0.6)).toBe('joy')
  })
})
