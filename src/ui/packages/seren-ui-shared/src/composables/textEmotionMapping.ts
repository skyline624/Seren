/**
 * Maps emotion labels emitted by a text classification model to the
 * Seren avatar emotion vocabulary (`neutral`, `joy`, `sad`, `anger`,
 * `surprise`, `relaxed`). Pure function — no I/O, fully deterministic.
 *
 * SRP: the file owns ONE concern (label translation + confidence gate).
 * OCP: add a new upstream label → one entry in `LABEL_TO_SEREN`.
 * KISS: no probabilistic merging across labels ; first past the
 *        threshold wins. Good enough for a fallback signal when the LLM
 *        didn't emit an explicit marker.
 */

/** Vocabulary emitted by the Seren avatar pipeline. Kept in sync with
 *  `LlmMarkerParser.cs` on the server (though the parser accepts any
 *  `\w+`, Seren renderers only know how to play these). */
export type SerenEmotion = 'neutral' | 'joy' | 'sad' | 'anger' | 'surprise' | 'relaxed'

/**
 * A single classifier prediction as returned by transformers.js
 * text-classification pipelines.
 */
export interface ClassifierLabel {
  label: string
  score: number
}

/**
 * Maps model-specific labels to the Seren vocabulary. Covers the labels
 * emitted by `Xenova/distilbert-base-uncased-emotion` (lower-case) and
 * a handful of close synonyms other popular models use — typos protected
 * by `toLowerCase()` at lookup.
 */
const LABEL_TO_SEREN: Readonly<Record<string, SerenEmotion>> = Object.freeze({
  sadness: 'sad',
  sad: 'sad',
  joy: 'joy',
  happiness: 'joy',
  happy: 'joy',
  love: 'joy',
  anger: 'anger',
  angry: 'anger',
  mad: 'anger',
  surprise: 'surprise',
  surprised: 'surprise',
  fear: 'surprise',
  afraid: 'surprise',
  neutral: 'neutral',
  calm: 'relaxed',
  relaxed: 'relaxed',
  content: 'relaxed',
  disgust: 'anger',
})

/**
 * Translate a ranked list of classifier predictions (highest score
 * first) into a Seren emotion. Returns `"neutral"` when no prediction
 * clears the confidence threshold or when no label is mappable.
 *
 * @param predictions - classifier output, highest-score first. The
 *   function walks the list until it finds a mappable label whose
 *   score meets or exceeds `confidenceThreshold`.
 * @param confidenceThreshold - minimum score to trust the prediction.
 *   Values below → "neutral".
 */
export function mapClassifierPredictionsToSeren(
  predictions: readonly ClassifierLabel[],
  confidenceThreshold: number,
): SerenEmotion {
  if (!Number.isFinite(confidenceThreshold)) {
    return 'neutral'
  }

  for (const prediction of predictions) {
    if (prediction.score < confidenceThreshold) {
      // Remaining predictions are <= current score; stop walking.
      break
    }
    const mapped = LABEL_TO_SEREN[prediction.label.toLowerCase()]
    if (mapped !== undefined) {
      return mapped
    }
  }

  return 'neutral'
}
