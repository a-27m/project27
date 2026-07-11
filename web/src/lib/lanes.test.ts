import { describe, expect, it } from 'vitest'
import { assignLanes } from './lanes'

describe('assignLanes', () => {
  it('keeps non-overlapping bars in one lane', () => {
    const { lanes, laneCount } = assignLanes([
      { uid: 1, start: 0, end: 10 },
      { uid: 2, start: 10, end: 20 },
      { uid: 3, start: 25, end: 30 },
    ])
    expect(laneCount).toBe(1)
    expect(lanes.every((l) => l.lane === 0)).toBe(true)
  })

  it('stacks overlapping bars', () => {
    const { lanes, laneCount } = assignLanes([
      { uid: 1, start: 0, end: 10 },
      { uid: 2, start: 5, end: 15 },
      { uid: 3, start: 8, end: 12 },
    ])
    expect(laneCount).toBe(3)
    expect(new Set(lanes.map((l) => l.lane)).size).toBe(3)
  })

  it('reuses freed lanes', () => {
    const { lanes, laneCount } = assignLanes([
      { uid: 1, start: 0, end: 10 },
      { uid: 2, start: 5, end: 8 },
      { uid: 3, start: 9, end: 12 }, // lane 1 is free again at 9
    ])
    expect(laneCount).toBe(2)
    expect(lanes.find((l) => l.uid === 3)!.lane).toBe(1)
  })
})
