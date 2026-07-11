import { describe, expect, it } from 'vitest'
import { layoutNetwork, type NetworkInput } from './network'

function task(uid: number, row: number, preds: number[] = [], summary = false): NetworkInput {
  return { uid, row, summary, predecessors: preds.map((p) => ({ predecessorUid: p })) }
}

describe('layoutNetwork', () => {
  it('ranks by longest predecessor chain', () => {
    const layout = layoutNetwork([
      task(1, 1),
      task(2, 2, [1]),
      task(3, 3, [1]),
      task(4, 4, [2, 3]),
      task(5, 5, [1, 4]), // longest chain 1→2→4→5 puts it in column 3
    ])
    const rank = (uid: number) => layout.nodes.find((n) => n.uid === uid)!.rank
    expect(rank(1)).toBe(0)
    expect(rank(2)).toBe(1)
    expect(rank(3)).toBe(1)
    expect(rank(4)).toBe(2)
    expect(rank(5)).toBe(3)
    expect(layout.columns).toBe(4)
    expect(layout.rows).toBe(2) // tasks 2 and 3 share a column
  })

  it('assigns lanes within a column by row order', () => {
    const layout = layoutNetwork([task(1, 1), task(2, 2), task(3, 3)])
    expect(layout.nodes.map((n) => [n.uid, n.rank, n.lane])).toEqual([
      [1, 0, 0],
      [2, 0, 1],
      [3, 0, 2],
    ])
  })

  it('excludes summaries and links through them', () => {
    const layout = layoutNetwork([
      task(10, 1, [], true), // summary
      task(1, 2, []),
      task(2, 3, [10, 1]), // the summary link disappears; rank comes from task 1
    ])
    expect(layout.nodes).toHaveLength(2)
    expect(layout.edges).toEqual([{ fromUid: 1, toUid: 2 }])
    expect(layout.nodes.find((n) => n.uid === 2)!.rank).toBe(1)
  })

  it('handles empty projects', () => {
    const layout = layoutNetwork([])
    expect(layout.columns).toBe(0)
    expect(layout.rows).toBe(0)
  })
})
