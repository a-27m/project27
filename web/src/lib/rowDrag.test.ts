import { describe, expect, it } from 'vitest'
import { beginRowDrag, dragMoved, dropTarget, moveRowDrag } from './rowDrag'

// Pre-order:
// Phase A(1,0) > A1(2,1), A2(3,1) ; Phase B(4,0) > B1(5,1)
const tasks = [
  { uid: 1, outlineLevel: 0 },
  { uid: 2, outlineLevel: 1 },
  { uid: 3, outlineLevel: 1 },
  { uid: 4, outlineLevel: 0 },
  { uid: 5, outlineLevel: 1 },
]
const none = new Set<number>()

describe('drag state machine', () => {
  it('is not a drag until it passes the threshold', () => {
    const drag = beginRowDrag(2, 100, 100)
    expect(dragMoved(drag)).toBe(false)
    expect(dragMoved(moveRowDrag(drag, 101, 101))).toBe(false)
    expect(dragMoved(moveRowDrag(drag, 100, 106))).toBe(true)
  })
})

describe('dropTarget — reorder within siblings', () => {
  it('moves A1 to after A2 (same parent)', () => {
    expect(dropTarget(tasks, 2, 3, 1, none)).toEqual({ uid: 2, parentUid: 1, at: 1, level: 1 })
  })

  it('rejects a no-op (drop A1 back into its own slot)', () => {
    expect(dropTarget(tasks, 2, 1, 1, none)).toBeNull()
  })
})

describe('dropTarget — reparent', () => {
  it('drops as first child of an expanded summary', () => {
    // Drag B1 up under Phase A's header (aboveUid = 1), indent hint = child level 1.
    expect(dropTarget(tasks, 5, 1, 1, none)).toEqual({ uid: 5, parentUid: 1, at: 0, level: 1 })
  })

  it('outdents A2 to a top-level task after everything', () => {
    // Drop A2 after B1 (aboveUid = 5), hint pulls left to level 0.
    expect(dropTarget(tasks, 3, 5, 0, none)).toEqual({ uid: 3, at: 2, level: 0 })
  })

  it('clamps the indent to the legal range for the gap', () => {
    // After B1 (level 1), the deepest legal is B1+1 = 2; a larger hint is clamped.
    expect(dropTarget(tasks, 3, 5, 9, none)).toEqual({ uid: 3, parentUid: 5, at: 0, level: 2 })
  })
})

describe('dropTarget — collapsed summary is opaque', () => {
  it('drops beside, never inside, a collapsed summary even when the hint says deeper', () => {
    // Phase B collapsed; drag A1, drop on Phase B's header wanting to go inside (hint 1).
    // Result: sibling of Phase B at top level, placed after B's hidden subtree.
    expect(dropTarget(tasks, 2, 4, 1, new Set([4]))).toEqual({ uid: 2, at: 2, level: 0 })
  })
})

describe('dropTarget — guards', () => {
  it('rejects dropping a summary into its own subtree', () => {
    expect(dropTarget(tasks, 1, 2, 1, none)).toBeNull()
  })

  it('rejects an unknown dragged uid', () => {
    expect(dropTarget(tasks, 99, 1, 0, none)).toBeNull()
  })
})

describe('dropTarget — top of list', () => {
  it('moves B1 to the very top as a top-level task', () => {
    expect(dropTarget(tasks, 5, null, 0, none)).toEqual({ uid: 5, at: 0, level: 0 })
  })
})
