import { describe, expect, it } from 'vitest'
import { positionOf, pruneNested, rangeBetween, siblingMove } from './outline'

// Pre-order: Phase(0) > A(1), B(1) ; Loose(0) ; Phase2(0) > C(1)
const tasks = [
  { uid: 1, outlineLevel: 0 },
  { uid: 2, outlineLevel: 1 },
  { uid: 3, outlineLevel: 1 },
  { uid: 4, outlineLevel: 0 },
  { uid: 5, outlineLevel: 0 },
  { uid: 6, outlineLevel: 1 },
]

describe('positionOf', () => {
  it('finds parents and sibling indexes', () => {
    expect(positionOf(tasks, 2)).toEqual({ parentUid: 1, index: 0, siblings: [2, 3] })
    expect(positionOf(tasks, 3)).toEqual({ parentUid: 1, index: 1, siblings: [2, 3] })
    expect(positionOf(tasks, 4)).toEqual({ parentUid: null, index: 1, siblings: [1, 4, 5] })
    expect(positionOf(tasks, 6)).toEqual({ parentUid: 5, index: 0, siblings: [6] })
    expect(positionOf(tasks, 99)).toBeNull()
  })
})

describe('siblingMove', () => {
  it('moves within siblings and stops at edges', () => {
    expect(siblingMove(tasks, 3, 'up')).toEqual({ uid: 3, parentUid: 1, at: 0 })
    expect(siblingMove(tasks, 2, 'up')).toBeNull()
    expect(siblingMove(tasks, 4, 'down')).toEqual({ uid: 4, at: 2 })
    expect(siblingMove(tasks, 5, 'down')).toBeNull()
    expect(siblingMove(tasks, 6, 'down')).toBeNull() // only child
  })
})

describe('rangeBetween', () => {
  it('returns the visible span inclusively, either direction', () => {
    expect(rangeBetween(tasks, 2, 4)).toEqual([2, 3, 4])
    expect(rangeBetween(tasks, 4, 2)).toEqual([2, 3, 4])
    expect(rangeBetween(tasks, 3, 3)).toEqual([3])
    expect(rangeBetween(tasks, 3, 99)).toEqual([])
  })
})

describe('pruneNested', () => {
  it('keeps only subtree roots', () => {
    expect(pruneNested(tasks, new Set([1, 2, 4]))).toEqual([1, 4]) // 2 is inside 1
    expect(pruneNested(tasks, new Set([2, 3]))).toEqual([2, 3]) // siblings both kept
    expect(pruneNested(tasks, new Set([5, 6]))).toEqual([5])
  })
})
