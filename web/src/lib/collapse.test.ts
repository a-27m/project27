import { describe, expect, it } from 'vitest'
import { hiddenUids, isCollapsible, visibleTasks } from './collapse'

// Pre-order:
// Phase(1,0) > A(2,1), Sub(3,1) > X(4,2), Y(5,2) ; Loose(6,0) ; Phase2(7,0) > C(8,1)
const tasks = [
  { uid: 1, outlineLevel: 0 },
  { uid: 2, outlineLevel: 1 },
  { uid: 3, outlineLevel: 1 },
  { uid: 4, outlineLevel: 2 },
  { uid: 5, outlineLevel: 2 },
  { uid: 6, outlineLevel: 0 },
  { uid: 7, outlineLevel: 0 },
  { uid: 8, outlineLevel: 1 },
]

describe('hiddenUids', () => {
  it('is empty when nothing is collapsed', () => {
    expect(hiddenUids(tasks, new Set())).toEqual(new Set())
  })

  it('hides the whole subtree of a collapsed summary', () => {
    expect(hiddenUids(tasks, new Set([1]))).toEqual(new Set([2, 3, 4, 5]))
    expect(hiddenUids(tasks, new Set([3]))).toEqual(new Set([4, 5]))
    expect(hiddenUids(tasks, new Set([7]))).toEqual(new Set([8]))
  })

  it('collapsing a non-summary hides nothing', () => {
    expect(hiddenUids(tasks, new Set([2]))).toEqual(new Set())
    expect(hiddenUids(tasks, new Set([6]))).toEqual(new Set())
  })

  it('handles a collapsed summary nested inside a collapsed one', () => {
    // Outer(1) already hides 2,3,4,5; the inner collapse of 3 adds nothing new.
    expect(hiddenUids(tasks, new Set([1, 3]))).toEqual(new Set([2, 3, 4, 5]))
  })

  it('combines independent collapsed summaries', () => {
    expect(hiddenUids(tasks, new Set([3, 7]))).toEqual(new Set([4, 5, 8]))
  })
})

describe('visibleTasks', () => {
  it('drops hidden descendants, preserving order', () => {
    expect(visibleTasks(tasks, new Set([3])).map((t) => t.uid)).toEqual([1, 2, 3, 6, 7, 8])
    expect(visibleTasks(tasks, new Set([1])).map((t) => t.uid)).toEqual([1, 6, 7, 8])
    expect(visibleTasks(tasks, new Set([1, 7])).map((t) => t.uid)).toEqual([1, 6, 7])
  })

  it('returns all tasks when nothing is collapsed', () => {
    expect(visibleTasks(tasks, new Set()).map((t) => t.uid)).toEqual([1, 2, 3, 4, 5, 6, 7, 8])
  })
})

describe('isCollapsible', () => {
  it('is true only for summaries', () => {
    expect(isCollapsible(tasks, 1)).toBe(true)
    expect(isCollapsible(tasks, 3)).toBe(true)
    expect(isCollapsible(tasks, 7)).toBe(true)
    expect(isCollapsible(tasks, 2)).toBe(false) // leaf
    expect(isCollapsible(tasks, 5)).toBe(false) // leaf, followed by shallower Loose
    expect(isCollapsible(tasks, 8)).toBe(false) // last task
    expect(isCollapsible(tasks, 99)).toBe(false) // unknown
  })
})
