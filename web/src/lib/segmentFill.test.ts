import { describe, expect, it } from 'vitest'
import { computeSegmentFills } from './segmentFill'

describe('computeSegmentFills', () => {
  it('fills a single segment proportionally', () => {
    expect(computeSegmentFills([100], 50)).toEqual([50])
  })

  it('fills the first segment fully before the second at 50% across two equal segments', () => {
    expect(computeSegmentFills([10, 10], 50)).toEqual([10, 0])
  })

  it('fills the first two fully and the third a quarter at 75% across three equal segments', () => {
    const [first, second, third] = computeSegmentFills([10, 10, 10], 75)
    expect(first).toBe(10)
    expect(second).toBe(10)
    expect(third).toBeCloseTo(2.5)
  })

  it('fills the first fully and the second half at 50% across three equal segments', () => {
    const [first, second, third] = computeSegmentFills([10, 10, 10], 50)
    expect(first).toBe(10)
    expect(second).toBeCloseTo(5)
    expect(third).toBe(0)
  })

  it('weights unequal segments by width', () => {
    const [first, second] = computeSegmentFills([57, 177], 75)
    expect(first).toBe(57)
    expect(second).toBeCloseTo(118.5)
  })

  it('returns all zeros at 0% complete', () => {
    expect(computeSegmentFills([10, 20], 0)).toEqual([0, 0])
  })

  it('fills every segment fully at 100% complete', () => {
    expect(computeSegmentFills([10, 20], 100)).toEqual([10, 20])
  })
})
