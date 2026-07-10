import { describe, expect, it } from 'vitest'
import {
  beginBarDrag,
  beginLinkDrag,
  endBarDrag,
  endLinkDrag,
  moveBarDrag,
  moveLinkDrag,
} from './drag'

describe('bar drag', () => {
  it('accumulates movement and reports the new bar start', () => {
    let drag = beginBarDrag(7, 100, 240)
    drag = moveBarDrag(drag, 148)
    const result = endBarDrag(drag)
    expect(result).toEqual({ uid: 7, newBarStartX: 288 })
  })

  it('treats sub-threshold movement as a click', () => {
    let drag = beginBarDrag(7, 100, 240)
    drag = moveBarDrag(drag, 102)
    expect(endBarDrag(drag)).toBeNull()
  })

  it('supports dragging left', () => {
    let drag = beginBarDrag(7, 100, 240)
    drag = moveBarDrag(drag, 40)
    expect(endBarDrag(drag)?.newBarStartX).toBe(180)
  })
})

describe('link drag', () => {
  it('tracks the hovered target and links on release', () => {
    let drag = beginLinkDrag(1, 10, 10)
    drag = moveLinkDrag(drag, 200, 60, 3)
    expect(drag.toUid).toBe(3)
    expect(endLinkDrag(drag)).toEqual({ predecessorUid: 1, successorUid: 3 })
  })

  it('never links a task to itself', () => {
    let drag = beginLinkDrag(1, 10, 10)
    drag = moveLinkDrag(drag, 12, 12, 1)
    expect(drag.toUid).toBeNull()
    expect(endLinkDrag(drag)).toBeNull()
  })

  it('releasing over nothing does nothing', () => {
    let drag = beginLinkDrag(1, 10, 10)
    drag = moveLinkDrag(drag, 500, 500, null)
    expect(endLinkDrag(drag)).toBeNull()
  })
})
