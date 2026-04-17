import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest'
import {
  applyVisemeLive2D,
  useLive2DLipsync,
  visemeToMouthForm,
  type VisemeTrackFrame,
} from './useLive2DLipsync'

interface MockCore {
  setParameterValueById: ReturnType<typeof vi.fn>
}

interface MockModel {
  internalModel: { coreModel: MockCore }
}

function makeMockModel(): MockModel {
  return {
    internalModel: {
      coreModel: {
        setParameterValueById: vi.fn(),
      },
    },
  }
}

describe('visemeToMouthForm', () => {
  it('spreads for Ee', () => {
    expect(visemeToMouthForm('ee')).toBe(1)
    expect(visemeToMouthForm('Ee')).toBe(1)
    expect(visemeToMouthForm('e')).toBe(1)
  })

  it('rounds for Ou/Oh/U/O', () => {
    expect(visemeToMouthForm('ou')).toBe(-1)
    expect(visemeToMouthForm('oh')).toBe(-1)
    expect(visemeToMouthForm('u')).toBe(-1)
    expect(visemeToMouthForm('o')).toBe(-1)
  })

  it('returns 0 for neutral Aa/Ih and unknown visemes', () => {
    expect(visemeToMouthForm('aa')).toBe(0)
    expect(visemeToMouthForm('ih')).toBe(0)
    expect(visemeToMouthForm('xyz')).toBe(0)
  })
})

describe('applyVisemeLive2D', () => {
  it('sets ParamMouthOpenY and ParamMouthForm for an Aa viseme', () => {
    const core = { setParameterValueById: vi.fn() }
    applyVisemeLive2D(core, 'aa', 0.7)
    expect(core.setParameterValueById).toHaveBeenCalledWith('ParamMouthOpenY', 0.7)
    expect(core.setParameterValueById).toHaveBeenCalledWith('ParamMouthForm', 0)
  })

  it('clamps the weight into [0, 1]', () => {
    const core = { setParameterValueById: vi.fn() }
    applyVisemeLive2D(core, 'aa', 1.5)
    expect(core.setParameterValueById).toHaveBeenCalledWith('ParamMouthOpenY', 1)
  })

  it('resets mouth on silence viseme', () => {
    const core = { setParameterValueById: vi.fn() }
    applyVisemeLive2D(core, '-', 0.8)
    expect(core.setParameterValueById).toHaveBeenCalledWith('ParamMouthOpenY', 0)
    expect(core.setParameterValueById).toHaveBeenCalledWith('ParamMouthForm', 0)
  })
})

describe('useLive2DLipsync.playTrack', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('schedules each frame at its startTime and resets at end', () => {
    const model = makeMockModel()
    const lipsync = useLive2DLipsync(() => model as unknown as never)

    const track: VisemeTrackFrame[] = [
      { viseme: 'aa', startTime: 0, duration: 0.1, weight: 0.8 },
      { viseme: 'ee', startTime: 0.1, duration: 0.1, weight: 0.6 },
    ]

    lipsync.playTrack(track)
    expect(lipsync.isActive.value).toBe(true)

    vi.advanceTimersByTime(0)
    expect(model.internalModel.coreModel.setParameterValueById)
      .toHaveBeenCalledWith('ParamMouthOpenY', 0.8)

    vi.advanceTimersByTime(100)
    expect(model.internalModel.coreModel.setParameterValueById)
      .toHaveBeenCalledWith('ParamMouthOpenY', 0.6)
    expect(model.internalModel.coreModel.setParameterValueById)
      .toHaveBeenCalledWith('ParamMouthForm', 1) // ee → spread

    vi.advanceTimersByTime(100) // reach end of last frame
    // The reset fires: mouth back to 0 on both params
    expect(model.internalModel.coreModel.setParameterValueById)
      .toHaveBeenCalledWith('ParamMouthOpenY', 0)
    expect(lipsync.isActive.value).toBe(false)
  })

  it('stop() clears pending timeouts and resets the mouth', () => {
    const model = makeMockModel()
    const lipsync = useLive2DLipsync(() => model as unknown as never)

    const track: VisemeTrackFrame[] = [
      { viseme: 'aa', startTime: 1, duration: 0.1, weight: 0.8 },
    ]
    lipsync.playTrack(track)
    lipsync.stop()

    // The scheduled viseme should never fire after stop().
    vi.advanceTimersByTime(2000)
    const calls = model.internalModel.coreModel.setParameterValueById.mock.calls
    // Expect: only the immediate reset from stop(), no "aa" opening.
    expect(calls.some(([, v]) => v === 0.8)).toBe(false)
    expect(lipsync.isActive.value).toBe(false)
  })

  it('playTrack with empty track is a no-op', () => {
    const model = makeMockModel()
    const lipsync = useLive2DLipsync(() => model as unknown as never)

    lipsync.playTrack([])
    expect(lipsync.isActive.value).toBe(false)
    expect(model.internalModel.coreModel.setParameterValueById).not.toHaveBeenCalled()
  })

  it('gracefully skips when the model is null', () => {
    const lipsync = useLive2DLipsync(() => null as unknown as never)
    expect(() => lipsync.playTrack([
      { viseme: 'aa', startTime: 0, duration: 0.1, weight: 0.5 },
    ])).not.toThrow()

    vi.advanceTimersByTime(200)
    expect(lipsync.isActive.value).toBe(false)
  })
})
